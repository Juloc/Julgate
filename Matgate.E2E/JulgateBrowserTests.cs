using Microsoft.Playwright;
using Xunit;

namespace Matgate.E2E;

public sealed class JulgateBrowserTests : IAsyncLifetime
{
    private IPlaywright _playwright = null!;
    private IBrowser _browser = null!;
    private readonly string _baseUrl = Environment.GetEnvironmentVariable("JULGATE_BASE_URL")
        ?? "http://127.0.0.1:8088";
    private readonly string _adminUser = Environment.GetEnvironmentVariable("JULGATE_ADMIN_USER") ?? "admin";
    private readonly string _adminPassword = Environment.GetEnvironmentVariable("JULGATE_ADMIN_PASSWORD")
        ?? throw new InvalidOperationException("JULGATE_ADMIN_PASSWORD is required for E2E tests.");
    private readonly string _artifactDirectory = Environment.GetEnvironmentVariable("JULGATE_E2E_ARTIFACTS")
        ?? Path.Combine("artifacts", "e2e");

    public async Task InitializeAsync()
    {
        Directory.CreateDirectory(_artifactDirectory);
        _playwright = await Playwright.CreateAsync();
        _browser = await _playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions
        {
            Headless = true
        });
    }

    public async Task DisposeAsync()
    {
        await _browser.DisposeAsync();
        _playwright.Dispose();
    }

    [Fact]
    public async Task ProtectedRoutesRedirectToLogin()
    {
        await using var context = await _browser.NewContextAsync();
        var page = await context.NewPageAsync();

        await page.GotoAsync($"{_baseUrl}/admin", new PageGotoOptions { WaitUntil = WaitUntilState.NetworkIdle });

        Assert.Contains("/login", page.Url, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ManifestAndLoginUseJulgateBranding()
    {
        await using var context = await _browser.NewContextAsync();
        var page = await context.NewPageAsync();

        var response = await page.GotoAsync($"{_baseUrl}/manifest.webmanifest");
        Assert.NotNull(response);
        Assert.True(response!.Ok);
        var manifest = await page.Locator("body").InnerTextAsync();
        Assert.Contains("Julgate", manifest, StringComparison.Ordinal);
        Assert.DoesNotContain("Matgate", manifest, StringComparison.Ordinal);

        await page.GotoAsync($"{_baseUrl}/login", new PageGotoOptions { WaitUntil = WaitUntilState.NetworkIdle });
        var html = await page.ContentAsync();
        Assert.Contains("Julgate", html, StringComparison.Ordinal);
        Assert.DoesNotContain(">Matgate<", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task LegacyBrowserStorageIsMigrated()
    {
        await using var context = await _browser.NewContextAsync();
        var page = await context.NewPageAsync();
        await page.GotoAsync($"{_baseUrl}/login");
        await page.EvaluateAsync("""
            () => {
              localStorage.setItem('matgate.shell.tabs.v1', '{"tabs":[]}');
              localStorage.setItem('matgate.workspace.panel.demo', 'files');
            }
            """);

        await page.ReloadAsync(new PageReloadOptions { WaitUntil = WaitUntilState.NetworkIdle });

        Assert.Null(await page.EvaluateAsync<string?>("() => localStorage.getItem('matgate.shell.tabs.v1')"));
        Assert.Equal("{\"tabs\":[]}", await page.EvaluateAsync<string?>("() => localStorage.getItem('julgate.shell.tabs.v1')"));
        Assert.Null(await page.EvaluateAsync<string?>("() => localStorage.getItem('matgate.workspace.panel.demo')"));
        Assert.Equal("files", await page.EvaluateAsync<string?>("() => localStorage.getItem('julgate.workspace.panel.demo')"));
    }

    [Theory]
    [InlineData("desktop", 1440, 1000)]
    [InlineData("tablet", 768, 1024)]
    [InlineData("phone", 390, 844)]
    public async Task AuthenticatedShellWorksWithoutHorizontalOverflow(string name, int width, int height)
    {
        await using var context = await _browser.NewContextAsync(new BrowserNewContextOptions
        {
            ViewportSize = new ViewportSize { Width = width, Height = height },
            IgnoreHTTPSErrors = true
        });
        var page = await context.NewPageAsync();
        await page.GotoAsync($"{_baseUrl}/login", new PageGotoOptions { WaitUntil = WaitUntilState.NetworkIdle });
        await page.FillAsync("input[name=username]", _adminUser);
        await page.FillAsync("input[name=password]", _adminPassword);
        await page.ClickAsync("button[type=submit]");
        await page.WaitForURLAsync(url => !url.Contains("/login", StringComparison.OrdinalIgnoreCase));
        await page.WaitForLoadStateAsync(LoadState.NetworkIdle);

        var bodyText = await page.Locator("body").InnerTextAsync();
        Assert.Contains("Julgate", bodyText, StringComparison.OrdinalIgnoreCase);
        Assert.True(await page.EvaluateAsync<bool>("() => document.documentElement.scrollWidth <= window.innerWidth + 1"));
        Assert.True(await page.Locator("nav, [role=navigation]").CountAsync() > 0);

        await page.ScreenshotAsync(new PageScreenshotOptions
        {
            Path = Path.Combine(_artifactDirectory, $"julgate-{name}.png"),
            FullPage = true
        });
    }
}
