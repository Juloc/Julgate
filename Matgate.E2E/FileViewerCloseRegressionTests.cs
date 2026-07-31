using Microsoft.Playwright;
using Xunit;

namespace Matgate.E2E;

public sealed class FileViewerCloseRegressionTests
{
    private readonly string _baseUrl = Environment.GetEnvironmentVariable("JULGATE_BASE_URL")
        ?? "http://127.0.0.1:8088";

    [Fact]
    public async Task EmbeddedFileViewerClose_ClosesOnlyTheDialog()
    {
        using var playwright = await Playwright.CreateAsync();
        await using var browser = await playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions
        {
            Headless = true
        });
        await using var context = await browser.NewContextAsync(new BrowserNewContextOptions
        {
            IgnoreHTTPSErrors = true
        });
        var page = await context.NewPageAsync();
        await page.GotoAsync(
            $"{_baseUrl}/login",
            new PageGotoOptions { WaitUntil = WaitUntilState.DOMContentLoaded });

        var originalUrl = page.Url;
        await page.EvaluateAsync("""
            () => {
              window.__julgateShellCloseCount = 0;
              document.addEventListener('click', event => {
                if (event.target instanceof Element
                    && event.target.closest('[data-file-viewer-close]')) {
                  window.__julgateShellCloseCount += 1;
                }
              });

              const dialog = document.createElement('dialog');
              dialog.id = 'file-viewer-close-regression';
              const close = document.createElement('a');
              close.href = '/sessions';
              close.dataset.fileViewerClose = '';
              close.textContent = 'Close';
              dialog.appendChild(close);
              document.body.appendChild(dialog);
              dialog.showModal();
            }
            """);

        var dialog = page.Locator("#file-viewer-close-regression");
        Assert.True(await dialog.EvaluateAsync<bool>("element => element.open"));

        await page.Locator("#file-viewer-close-regression [data-file-viewer-close]").ClickAsync();

        Assert.False(await dialog.EvaluateAsync<bool>("element => element.open"));
        Assert.Equal(
            0,
            await page.EvaluateAsync<int>("() => window.__julgateShellCloseCount"));
        Assert.Equal(originalUrl, page.Url);
    }
}
