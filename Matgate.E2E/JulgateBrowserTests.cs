using System.Net;
using System.Text.RegularExpressions;
using Microsoft.Playwright;
using Xunit;

namespace Matgate.E2E;

public sealed partial class JulgateBrowserTests
{
    private readonly string _baseUrl = Environment.GetEnvironmentVariable("JULGATE_BASE_URL")
        ?? "http://127.0.0.1:8088";
    private readonly string _adminUser = Environment.GetEnvironmentVariable("JULGATE_ADMIN_USER") ?? "admin";
    private readonly string _adminPassword = Environment.GetEnvironmentVariable("JULGATE_ADMIN_PASSWORD")
        ?? throw new InvalidOperationException("JULGATE_ADMIN_PASSWORD is required for E2E tests.");
    private readonly string _artifactDirectory = Environment.GetEnvironmentVariable("JULGATE_E2E_ARTIFACTS")
        ?? Path.Combine("artifacts", "e2e");

    [Fact]
    public async Task CompleteBrowserAcceptance()
    {
        Directory.CreateDirectory(_artifactDirectory);
        using var playwright = await Playwright.CreateAsync();
        await using var browser = await playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions
        {
            Headless = true
        });
        await using var context = await browser.NewContextAsync(new BrowserNewContextOptions
        {
            ViewportSize = new ViewportSize { Width = 1440, Height = 1000 },
            IgnoreHTTPSErrors = true
        });
        context.SetDefaultTimeout(5_000);
        context.SetDefaultNavigationTimeout(15_000);

        var loginPage = await context.NewPageAsync();
        Console.WriteLine("Checking anonymous protection and branding.");
        await VerifyAnonymousRoutesAsync(loginPage);
        await VerifyLegacyStorageMigrationAsync(loginPage);

        Console.WriteLine("Signing in once for all authenticated checks.");
        await LoginAsync(loginPage);
        await loginPage.CloseAsync();

        Console.WriteLine("Checking authenticated route responses.");
        var authenticatedPages = await FetchAuthenticatedPagesAsync(context);

        Console.WriteLine("Checking representative responsive pages.");
        var page = await context.NewPageAsync();
        await VerifyResponsivePagesAsync(page, authenticatedPages);
    }

    private async Task VerifyAnonymousRoutesAsync(IPage page)
    {
        var protectedResponse = await page.GotoAsync(
            $"{_baseUrl}/admin/users",
            new PageGotoOptions { WaitUntil = WaitUntilState.DOMContentLoaded });
        Assert.NotNull(protectedResponse);
        Assert.Contains("/login", page.Url, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("returnUrl", page.Url, StringComparison.OrdinalIgnoreCase);

        var manifestResponse = await page.GotoAsync(
            $"{_baseUrl}/manifest.webmanifest",
            new PageGotoOptions { WaitUntil = WaitUntilState.DOMContentLoaded });
        Assert.NotNull(manifestResponse);
        Assert.True(manifestResponse!.Ok);
        var manifest = await page.Locator("body").InnerTextAsync();
        Assert.Contains("Julgate", manifest, StringComparison.Ordinal);
        Assert.DoesNotContain("Matgate", manifest, StringComparison.Ordinal);

        await page.GotoAsync(
            $"{_baseUrl}/login",
            new PageGotoOptions { WaitUntil = WaitUntilState.DOMContentLoaded });
        var loginHtml = await page.ContentAsync();
        Assert.Contains("Julgate", loginHtml, StringComparison.Ordinal);
        Assert.DoesNotContain(">Matgate<", loginHtml, StringComparison.Ordinal);
    }

    private async Task VerifyLegacyStorageMigrationAsync(IPage page)
    {
        await page.GotoAsync(
            $"{_baseUrl}/login",
            new PageGotoOptions { WaitUntil = WaitUntilState.DOMContentLoaded });
        await page.EvaluateAsync("""
            () => {
              localStorage.setItem('matgate.shell.tabs.v1', '{"tabs":[]}');
              localStorage.setItem('matgate.workspace.panel.demo', 'files');
            }
            """);
        await page.ReloadAsync(new PageReloadOptions { WaitUntil = WaitUntilState.DOMContentLoaded });

        Assert.Null(await page.EvaluateAsync<string?>("() => localStorage.getItem('matgate.shell.tabs.v1')"));
        Assert.Equal("{\"tabs\":[]}", await page.EvaluateAsync<string?>("() => localStorage.getItem('julgate.shell.tabs.v1')"));
        Assert.Null(await page.EvaluateAsync<string?>("() => localStorage.getItem('matgate.workspace.panel.demo')"));
        Assert.Equal("files", await page.EvaluateAsync<string?>("() => localStorage.getItem('julgate.workspace.panel.demo')"));
    }

    private async Task LoginAsync(IPage page)
    {
        await page.GotoAsync(
            $"{_baseUrl}/login",
            new PageGotoOptions { WaitUntil = WaitUntilState.DOMContentLoaded });
        await page.FillAsync("input[name=username]", _adminUser);
        await page.FillAsync("input[name=password]", _adminPassword);
        var response = await page.RunAndWaitForResponseAsync(
            async () => await page.ClickAsync(
                "button[type=submit]",
                new PageClickOptions { NoWaitAfter = true }),
            candidate => candidate.Request.Method == "POST"
                && candidate.Url.EndsWith("/login", StringComparison.OrdinalIgnoreCase));
        Assert.NotEqual(429, response.Status);
        Assert.True(response.Status is >= 200 and < 400);

        var cookies = await page.Context.CookiesAsync(_baseUrl);
        Assert.Contains(cookies, cookie => cookie.HttpOnly && !string.IsNullOrWhiteSpace(cookie.Value));
    }

    private async Task<IReadOnlyDictionary<string, string>> FetchAuthenticatedPagesAsync(IBrowserContext context)
    {
        var routes = new[]
        {
            "/",
            "/admin/users",
            "/admin/servers",
            "/account",
            "/workspaces",
            "/sessions",
            "/about"
        };
        var cookies = await context.CookiesAsync(_baseUrl);
        var cookieHeader = string.Join("; ", cookies.Select(cookie => $"{cookie.Name}={cookie.Value}"));
        Assert.False(string.IsNullOrWhiteSpace(cookieHeader));

        using var handler = new HttpClientHandler { AllowAutoRedirect = false };
        using var client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(15) };
        client.DefaultRequestHeaders.TryAddWithoutValidation("Cookie", cookieHeader);
        var pages = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var route in routes)
        {
            using var response = await client.GetAsync($"{_baseUrl}{route}");
            Assert.True(
                response.StatusCode is >= HttpStatusCode.OK and < HttpStatusCode.BadRequest,
                $"{route} returned HTTP {(int)response.StatusCode}.");
            Assert.DoesNotContain(
                "/login",
                response.Headers.Location?.ToString() ?? string.Empty,
                StringComparison.OrdinalIgnoreCase);

            var html = await response.Content.ReadAsStringAsync();
            Assert.Contains("Julgate", html, StringComparison.OrdinalIgnoreCase);
            pages[route] = html;
        }

        return pages;
    }

    private async Task VerifyResponsivePagesAsync(
        IPage page,
        IReadOnlyDictionary<string, string> authenticatedPages)
    {
        var cases = new[]
        {
            (Name: "desktop", Route: "/admin/users", Width: 1440, Height: 1000),
            (Name: "tablet", Route: "/workspaces", Width: 768, Height: 1024),
            (Name: "phone", Route: "/about", Width: 390, Height: 844)
        };

        foreach (var item in cases)
        {
            Console.WriteLine($"Rendering {item.Route} at {item.Width}x{item.Height}.");
            await page.SetViewportSizeAsync(item.Width, item.Height);
            var html = PrepareStaticLayoutHtml(authenticatedPages[item.Route]);
            await page.SetContentAsync(
                html,
                new PageSetContentOptions
                {
                    WaitUntil = WaitUntilState.DOMContentLoaded,
                    Timeout = 15_000
                });
            await page.WaitForTimeoutAsync(250);
            await AssertPageIsResponsiveAsync(page);
            await page.ScreenshotAsync(new PageScreenshotOptions
            {
                Path = Path.Combine(_artifactDirectory, $"julgate-{item.Name}.png"),
                Timeout = 10_000
            });
        }
    }

    private string PrepareStaticLayoutHtml(string html)
    {
        var withoutScripts = ScriptElementRegex().Replace(html, string.Empty);
        return withoutScripts.Replace(
            "<head>",
            $"<head><base href=\"{_baseUrl}/\">",
            StringComparison.OrdinalIgnoreCase);
    }

    private static async Task AssertPageIsResponsiveAsync(IPage page)
    {
        var bodyText = await page.Locator("body").InnerTextAsync();
        Assert.Contains("Julgate", bodyText, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("MATGATE", bodyText, StringComparison.Ordinal);
        Assert.True(await page.EvaluateAsync<bool>(
            "() => document.documentElement.scrollWidth <= window.innerWidth + 1"));
    }

    [GeneratedRegex("<script\\b[^>]*>[\\s\\S]*?</script>", RegexOptions.IgnoreCase)]
    private static partial Regex ScriptElementRegex();
}
