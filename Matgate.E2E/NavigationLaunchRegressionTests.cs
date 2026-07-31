using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Playwright;
using Xunit;

namespace Matgate.E2E;

public sealed class NavigationLaunchRegressionTests
{
    private readonly string _baseUrl = Environment.GetEnvironmentVariable("JULGATE_BASE_URL")
        ?? "http://127.0.0.1:8088";
    private readonly string _adminUser = Environment.GetEnvironmentVariable("JULGATE_ADMIN_USER") ?? "admin";
    private readonly string _adminPassword = Environment.GetEnvironmentVariable("JULGATE_ADMIN_PASSWORD")
        ?? throw new InvalidOperationException("JULGATE_ADMIN_PASSWORD is required for E2E tests.");

    [Fact]
    public async Task ShellNavigationLaunchAndEditorActions_WorkInTheRunningApplication()
    {
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
        context.SetDefaultTimeout(10_000);
        context.SetDefaultNavigationTimeout(20_000);

        var page = await context.NewPageAsync();
        await LoginAsync(page);

        await VerifyConsistentEditorActionsAsync(page);
        await VerifySameOriginEmbeddedPagesAsync(page);
        await VerifyBrandedCsrfLaunchAsync(context);
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

        Assert.True(response.Status is >= 200 and < 400, $"Login returned HTTP {response.Status}.");
        var cookies = await ContextCookiesAsync(page.Context);
        Assert.NotEmpty(cookies);
    }

    private async Task VerifyConsistentEditorActionsAsync(IPage page)
    {
        await page.GotoAsync(
            $"{_baseUrl}/",
            new PageGotoOptions { WaitUntil = WaitUntilState.DOMContentLoaded });

        var createAction = page.Locator("a[href='/admin/servers/new']").First;
        await createAction.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible });
        Assert.Equal("1", await createAction.GetAttributeAsync("data-shell-open-tab"));
        Assert.Contains(
            "connection",
            await createAction.GetAttributeAsync("data-shell-title") ?? string.Empty,
            StringComparison.OrdinalIgnoreCase);

        var editAction = page.Locator(".connection-choice .julgate-server-edit-action").First;
        await editAction.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible });
        var href = await editAction.GetAttributeAsync("href");
        Assert.Matches("^/admin/servers/[0-9a-f-]{36}$", href ?? string.Empty);
        Assert.Equal("1", await editAction.GetAttributeAsync("data-shell-open-tab"));
        Assert.Contains(
            "connection",
            await editAction.GetAttributeAsync("data-shell-title") ?? string.Empty,
            StringComparison.OrdinalIgnoreCase);
    }

    private async Task VerifySameOriginEmbeddedPagesAsync(IPage page)
    {
        await page.GotoAsync(
            $"{_baseUrl}/about",
            new PageGotoOptions { WaitUntil = WaitUntilState.DOMContentLoaded });

        var routes = new[]
        {
            "/workspaces?embed=1",
            "/admin/servers?embed=1",
            "/admin/users?embed=1",
            "/account?embed=1"
        };

        foreach (var route in routes)
        {
            await page.EvaluateAsync(
                "url => { const old = document.getElementById('shell-regression-frame'); if (old) old.remove(); const frame = document.createElement('iframe'); frame.id = 'shell-regression-frame'; frame.src = url; frame.style.width = '900px'; frame.style.height = '700px'; document.body.append(frame); }",
                $"{_baseUrl}{route}");

            var frameElement = await page.Locator("#shell-regression-frame").ElementHandleAsync();
            Assert.NotNull(frameElement);
            var frame = await frameElement!.ContentFrameAsync();
            Assert.NotNull(frame);
            await frame!.WaitForLoadStateAsync(LoadState.DOMContentLoaded);
            var bodyText = await frame.Locator("body").InnerTextAsync();
            Assert.False(string.IsNullOrWhiteSpace(bodyText), $"{route} rendered an empty iframe.");
            Assert.DoesNotContain("refused to connect", bodyText, StringComparison.OrdinalIgnoreCase);
        }
    }

    private async Task VerifyBrandedCsrfLaunchAsync(IBrowserContext context)
    {
        var cookies = await ContextCookiesAsync(context);
        var cookieHeader = string.Join("; ", cookies.Select(cookie => $"{cookie.Name}={cookie.Value}"));

        using var handler = new HttpClientHandler { AllowAutoRedirect = true };
        using var client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(20) };
        client.DefaultRequestHeaders.TryAddWithoutValidation("Cookie", cookieHeader);

        var homeHtml = await client.GetStringAsync($"{_baseUrl}/");
        var csrfMatch = Regex.Match(homeHtml, "const csrfToken = (?<value>\\\"(?:\\\\.|[^\\\"])*\\\");");
        var serverMatch = Regex.Match(homeHtml, "data-server-id=\\\"(?<id>[0-9a-fA-F-]{36})\\\"");
        Assert.True(csrfMatch.Success, "The rendered shell did not contain a CSRF token.");
        Assert.True(serverMatch.Success, "The rendered shell did not contain a launchable server.");

        var csrfToken = JsonSerializer.Deserialize<string>(csrfMatch.Groups["value"].Value);
        Assert.False(string.IsNullOrWhiteSpace(csrfToken));

        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            $"{_baseUrl}/api/connections/{serverMatch.Groups["id"].Value}/launch");
        request.Headers.TryAddWithoutValidation("X-Julgate-Csrf", csrfToken);
        request.Content = new StringContent(string.Empty);

        using var response = await client.SendAsync(request);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("encryptedData", body, StringComparison.Ordinal);
        Assert.Contains("connectionName", body, StringComparison.Ordinal);
    }

    private static async Task<IReadOnlyList<BrowserContextCookiesResult>> ContextCookiesAsync(
        IBrowserContext context)
    {
        return await context.CookiesAsync();
    }
}
