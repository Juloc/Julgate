using Microsoft.Playwright;
using Xunit;

namespace Matgate.E2E;

public sealed class JulgateBrowserTests
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
        context.SetDefaultNavigationTimeout(10_000);
        var page = await context.NewPageAsync();

        Console.WriteLine("Checking anonymous protection and branding.");
        await VerifyAnonymousRoutesAsync(page);
        await VerifyLegacyStorageMigrationAsync(page);

        Console.WriteLine("Signing in once for all authenticated checks.");
        await LoginAsync(page);

        Console.WriteLine("Checking responsive shell screenshots.");
        await VerifyResponsiveShellAsync(page);

        Console.WriteLine("Checking primary authenticated routes.");
        await VerifyPrimaryPagesAsync(page);
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
            async () => await page.ClickAsync("button[type=submit]"),
            candidate => candidate.Request.Method == "POST"
                && candidate.Url.EndsWith("/login", StringComparison.OrdinalIgnoreCase));
        Assert.NotEqual(429, response.Status);
        await page.WaitForURLAsync(url => !url.Contains("/login", StringComparison.OrdinalIgnoreCase));
    }

    private async Task VerifyResponsiveShellAsync(IPage page)
    {
        var viewports = new[]
        {
            (Name: "desktop", Width: 1440, Height: 1000),
            (Name: "tablet", Width: 768, Height: 1024),
            (Name: "phone", Width: 390, Height: 844)
        };

        foreach (var viewport in viewports)
        {
            await page.SetViewportSizeAsync(viewport.Width, viewport.Height);
            await NavigateAuthenticatedAsync(page, "/");
            await AssertPageIsResponsiveAsync(page);
            await page.ScreenshotAsync(new PageScreenshotOptions
            {
                Path = Path.Combine(_artifactDirectory, $"julgate-{viewport.Name}.png"),
                FullPage = true
            });
        }
    }

    private async Task VerifyPrimaryPagesAsync(IPage page)
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
        var viewports = new[]
        {
            (Width: 1440, Height: 1000),
            (Width: 390, Height: 844)
        };

        foreach (var viewport in viewports)
        {
            await page.SetViewportSizeAsync(viewport.Width, viewport.Height);
            foreach (var route in routes)
            {
                Console.WriteLine($"Checking {route} at {viewport.Width}x{viewport.Height}.");
                var response = await NavigateAuthenticatedAsync(page, route);
                Assert.NotNull(response);
                Assert.True(response!.Status < 400, $"{route} returned HTTP {response.Status}.");
                Assert.DoesNotContain("/login", page.Url, StringComparison.OrdinalIgnoreCase);
                await AssertPageIsResponsiveAsync(page);
            }
        }
    }

    private async Task<IResponse?> NavigateAuthenticatedAsync(IPage page, string route)
    {
        var response = await page.GotoAsync(
            $"{_baseUrl}{route}",
            new PageGotoOptions
            {
                WaitUntil = WaitUntilState.Commit,
                Timeout = 10_000
            });
        await page.WaitForFunctionAsync(
            "() => document.body && document.body.innerText.trim().length > 0 && document.querySelector('nav, [role=\"navigation\"]')",
            null,
            new PageWaitForFunctionOptions { Timeout = 10_000 });
        return response;
    }

    private static async Task AssertPageIsResponsiveAsync(IPage page)
    {
        await page.WaitForFunctionAsync(
            "() => !document.body.innerText.includes('MATGATE')",
            null,
            new PageWaitForFunctionOptions { Timeout = 5_000 });
        var bodyText = await page.Locator("body").InnerTextAsync();
        Assert.Contains("Julgate", bodyText, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("MATGATE", bodyText, StringComparison.Ordinal);
        Assert.True(await page.EvaluateAsync<bool>(
            "() => document.documentElement.scrollWidth <= window.innerWidth + 1"));
    }
}
