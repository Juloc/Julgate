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

        await VerifyAnonymousRoutesAsync(browser);
        await VerifyLegacyStorageMigrationAsync(browser);

        var storageStatePath = Path.Combine(_artifactDirectory, "admin-storage-state.json");
        try
        {
            await CreateAuthenticatedStateAsync(browser, storageStatePath);
            await VerifyResponsiveShellAsync(browser, storageStatePath);
            await VerifyPrimaryPagesAsync(browser, storageStatePath);
        }
        finally
        {
            if (File.Exists(storageStatePath))
            {
                File.Delete(storageStatePath);
            }
        }
    }

    private async Task VerifyAnonymousRoutesAsync(IBrowser browser)
    {
        await using var context = await browser.NewContextAsync();
        context.SetDefaultTimeout(10_000);
        context.SetDefaultNavigationTimeout(20_000);
        var page = await context.NewPageAsync();

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

    private async Task VerifyLegacyStorageMigrationAsync(IBrowser browser)
    {
        await using var context = await browser.NewContextAsync();
        context.SetDefaultTimeout(10_000);
        context.SetDefaultNavigationTimeout(20_000);
        var page = await context.NewPageAsync();
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

    private async Task CreateAuthenticatedStateAsync(IBrowser browser, string storageStatePath)
    {
        await using var context = await browser.NewContextAsync();
        context.SetDefaultTimeout(10_000);
        context.SetDefaultNavigationTimeout(20_000);
        var page = await context.NewPageAsync();
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
        await context.StorageStateAsync(new BrowserContextStorageStateOptions
        {
            Path = storageStatePath
        });
    }

    private async Task VerifyResponsiveShellAsync(IBrowser browser, string storageStatePath)
    {
        var viewports = new[]
        {
            (Name: "desktop", Width: 1440, Height: 1000),
            (Name: "tablet", Width: 768, Height: 1024),
            (Name: "phone", Width: 390, Height: 844)
        };

        foreach (var viewport in viewports)
        {
            await using var context = await NewAuthenticatedContextAsync(
                browser,
                storageStatePath,
                viewport.Width,
                viewport.Height);
            var page = await context.NewPageAsync();
            await page.GotoAsync(
                _baseUrl,
                new PageGotoOptions { WaitUntil = WaitUntilState.DOMContentLoaded });
            await AssertPageIsResponsiveAsync(page);
            Assert.True(await page.Locator("nav, [role=navigation]").CountAsync() > 0);
            await page.ScreenshotAsync(new PageScreenshotOptions
            {
                Path = Path.Combine(_artifactDirectory, $"julgate-{viewport.Name}.png"),
                FullPage = true
            });
        }
    }

    private async Task VerifyPrimaryPagesAsync(IBrowser browser, string storageStatePath)
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
            await using var context = await NewAuthenticatedContextAsync(
                browser,
                storageStatePath,
                viewport.Width,
                viewport.Height);
            var page = await context.NewPageAsync();

            foreach (var route in routes)
            {
                var response = await page.GotoAsync(
                    $"{_baseUrl}{route}",
                    new PageGotoOptions { WaitUntil = WaitUntilState.DOMContentLoaded });
                Assert.NotNull(response);
                Assert.True(response!.Status < 400, $"{route} returned HTTP {response.Status}.");
                Assert.DoesNotContain("/login", page.Url, StringComparison.OrdinalIgnoreCase);
                await AssertPageIsResponsiveAsync(page);
            }
        }
    }

    private static async Task<IBrowserContext> NewAuthenticatedContextAsync(
        IBrowser browser,
        string storageStatePath,
        int width,
        int height)
    {
        var context = await browser.NewContextAsync(new BrowserNewContextOptions
        {
            ViewportSize = new ViewportSize { Width = width, Height = height },
            IgnoreHTTPSErrors = true,
            StorageStatePath = storageStatePath
        });
        context.SetDefaultTimeout(10_000);
        context.SetDefaultNavigationTimeout(20_000);
        return context;
    }

    private static async Task AssertPageIsResponsiveAsync(IPage page)
    {
        await page.WaitForFunctionAsync(
            "() => !document.body.innerText.includes('MATGATE')",
            null,
            new PageWaitForFunctionOptions { Timeout = 10_000 });
        var bodyText = await page.Locator("body").InnerTextAsync();
        Assert.Contains("Julgate", bodyText, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("MATGATE", bodyText, StringComparison.Ordinal);
        Assert.True(await page.EvaluateAsync<bool>(
            "() => document.documentElement.scrollWidth <= window.innerWidth + 1"));
    }
}
