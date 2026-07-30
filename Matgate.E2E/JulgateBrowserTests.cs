using Microsoft.Playwright;
using Xunit;

namespace Matgate.E2E;

public sealed class JulgateBrowserTests : IAsyncLifetime
{
    private IPlaywright _playwright = null!;
    private IBrowser _browser = null!;
    private string _storageStatePath = null!;
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
        _storageStatePath = Path.Combine(_artifactDirectory, "admin-storage-state.json");
        _playwright = await Playwright.CreateAsync();
        _browser = await _playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions
        {
            Headless = true
        });

        await using var context = await _browser.NewContextAsync();
        var page = await context.NewPageAsync();
        await page.GotoAsync($"{_baseUrl}/login", new PageGotoOptions { WaitUntil = WaitUntilState.NetworkIdle });
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
            Path = _storageStatePath
        });
    }

    public async Task DisposeAsync()
    {
        await _browser.DisposeAsync();
        _playwright.Dispose();
        if (File.Exists(_storageStatePath))
        {
            File.Delete(_storageStatePath);
        }
    }

    [Fact]
    public async Task ProtectedRoutesRedirectToLogin()
    {
        await using var context = await _browser.NewContextAsync();
        var page = await context.NewPageAsync();

        var response = await page.GotoAsync(
            $"{_baseUrl}/admin/users",
            new PageGotoOptions { WaitUntil = WaitUntilState.NetworkIdle });

        Assert.NotNull(response);
        Assert.Contains("/login", page.Url, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("returnUrl", page.Url, StringComparison.OrdinalIgnoreCase);
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
        await using var context = await NewAuthenticatedContextAsync(width, height);
        var page = await context.NewPageAsync();
        await page.GotoAsync(_baseUrl, new PageGotoOptions { WaitUntil = WaitUntilState.NetworkIdle });

        await AssertPageIsResponsiveAsync(page);
        Assert.True(await page.Locator("nav, [role=navigation]").CountAsync() > 0);

        await page.ScreenshotAsync(new PageScreenshotOptions
        {
            Path = Path.Combine(_artifactDirectory, $"julgate-{name}.png"),
            FullPage = true
        });
    }

    [Theory]
    [InlineData("/", 1440, 1000)]
    [InlineData("/admin/users", 1440, 1000)]
    [InlineData("/admin/servers", 1440, 1000)]
    [InlineData("/account", 1440, 1000)]
    [InlineData("/workspaces", 1440, 1000)]
    [InlineData("/sessions", 1440, 1000)]
    [InlineData("/about", 1440, 1000)]
    [InlineData("/", 390, 844)]
    [InlineData("/admin/users", 390, 844)]
    [InlineData("/admin/servers", 390, 844)]
    [InlineData("/account", 390, 844)]
    [InlineData("/workspaces", 390, 844)]
    [InlineData("/sessions", 390, 844)]
    [InlineData("/about", 390, 844)]
    public async Task PrimaryPagesRenderOnDesktopAndPhone(string route, int width, int height)
    {
        await using var context = await NewAuthenticatedContextAsync(width, height);
        var page = await context.NewPageAsync();
        var response = await page.GotoAsync($"{_baseUrl}{route}", new PageGotoOptions
        {
            WaitUntil = WaitUntilState.NetworkIdle
        });

        Assert.NotNull(response);
        Assert.True(response!.Status < 400, $"{route} returned HTTP {response.Status}.");
        Assert.DoesNotContain("/login", page.Url, StringComparison.OrdinalIgnoreCase);
        await AssertPageIsResponsiveAsync(page);
    }

    private Task<IBrowserContext> NewAuthenticatedContextAsync(int width, int height)
    {
        return _browser.NewContextAsync(new BrowserNewContextOptions
        {
            ViewportSize = new ViewportSize { Width = width, Height = height },
            IgnoreHTTPSErrors = true,
            StorageStatePath = _storageStatePath
        });
    }

    private static async Task AssertPageIsResponsiveAsync(IPage page)
    {
        await page.WaitForFunctionAsync("() => !document.body.innerText.includes('MATGATE')");
        var bodyText = await page.Locator("body").InnerTextAsync();
        Assert.Contains("Julgate", bodyText, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("MATGATE", bodyText, StringComparison.Ordinal);
        Assert.True(await page.EvaluateAsync<bool>(
            "() => document.documentElement.scrollWidth <= window.innerWidth + 1"));
    }
}
