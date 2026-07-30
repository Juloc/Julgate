using System.Net;
using System.Security.Cryptography;
using System.Text;
using Matgate.Services;
using Matgate.Web;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace Matgate.Tests;

public sealed class CompletionHardeningTests
{
    [Theory]
    [InlineData("../secret")]
    [InlineData("folder/../secret")]
    [InlineData("folder%2f..%2fsecret")]
    [InlineData("folder%252f..%252fsecret")]
    [InlineData("folder\\..\\secret")]
    public void FileGateway_RejectsTraversalAtServiceBoundary(string path)
    {
        Assert.Throws<InvalidOperationException>(() => FileGatewaySecurityDecorator.NormalizeVirtualPath(path));
    }

    [Theory]
    [InlineData(null, "/")]
    [InlineData("/", "/")]
    [InlineData("folder/file.txt", "/folder/file.txt")]
    [InlineData("/folder name/file.txt", "/folder name/file.txt")]
    public void FileGateway_NormalizesSafePaths(string? path, string expected)
    {
        Assert.Equal(expected, FileGatewaySecurityDecorator.NormalizeVirtualPath(path));
    }

    [Theory]
    [InlineData("../file.txt")]
    [InlineData("folder/file.txt")]
    [InlineData("folder\\file.txt")]
    [InlineData("..")]
    public void FileGateway_RejectsUnsafeLeafNames(string name)
    {
        Assert.Throws<InvalidOperationException>(() => FileGatewaySecurityDecorator.NormalizeLeafName(name));
    }

    [Fact]
    public void ArchiveBudget_RejectsExpandedSizeOverflow()
    {
        using var scope = FileTransferBudget.Begin(maxBytes: 4, maxEntries: 10);
        FileTransferBudget.ConsumeBytes(4);
        Assert.Throws<FileTransferLimitExceededException>(() => FileTransferBudget.ConsumeBytes(1));
    }

    [Fact]
    public void ArchiveBudget_RejectsEntryOverflow()
    {
        using var scope = FileTransferBudget.Begin(maxBytes: 1024, maxEntries: 1);
        FileTransferBudget.ConsumeEntry();
        Assert.Throws<FileTransferLimitExceededException>(FileTransferBudget.ConsumeEntry);
    }

    [Fact]
    public async Task BoundedReadStream_RejectsChunkedOverflow()
    {
        await using var source = new MemoryStream(Encoding.UTF8.GetBytes("12345"));
        await using var bounded = new BoundedReadStream(source, 4, countAgainstArchiveBudget: false);
        var buffer = new byte[8];

        await Assert.ThrowsAsync<FileTransferLimitExceededException>(async () =>
            await bounded.ReadAsync(buffer));
    }

    [Fact]
    public void CredentialProtector_RotatesFromPreviousKey()
    {
        var oldKey = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
        var newKey = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
        using var oldProtector = new CredentialProtector(oldKey);
        var oldCiphertext = oldProtector.Protect("rotated-secret");

        using var rotated = new CredentialProtector(newKey, oldKey);
        Assert.Equal("rotated-secret", rotated.Unprotect(oldCiphertext));

        var newCiphertext = rotated.Protect(rotated.Unprotect(oldCiphertext));
        Assert.NotEqual(oldCiphertext, newCiphertext);
        Assert.Throws<InvalidOperationException>(() => oldProtector.Unprotect(newCiphertext));
    }

    [Fact]
    public async Task WebsiteProxy_RejectsDnsNamesAndAllowsPrivateLiteralIp()
    {
        Assert.False(await WebsiteProxyTargetGuardMiddleware.IsAllowedAsync(
            new Uri("https://nas.home/"),
            CancellationToken.None));
        Assert.True(await WebsiteProxyTargetGuardMiddleware.IsAllowedAsync(
            new Uri("https://192.168.1.10/"),
            CancellationToken.None));
        Assert.False(await WebsiteProxyTargetGuardMiddleware.IsAllowedAsync(
            new Uri("http://169.254.169.254/"),
            CancellationToken.None));
        Assert.True(WebsiteProxyTargetGuardMiddleware.IsDisallowedAddress(IPAddress.Loopback));
    }

    [Fact]
    public void Branding_ReplacesProductAndStorageNames()
    {
        var branded = Ae01ThemeMiddleware.ApplyBranding(
            "Matgate MATGATE matgate.shell.tabs.v1 matgate.workspace.panel.home matgate-archive");

        Assert.DoesNotContain("Matgate", branded, StringComparison.Ordinal);
        Assert.DoesNotContain("MATGATE", branded, StringComparison.Ordinal);
        Assert.DoesNotContain("matgate.shell.tabs.v1", branded, StringComparison.Ordinal);
        Assert.Contains("Julgate JULGATE julgate.shell.tabs.v1 julgate.workspace.panel.home julgate-archive", branded, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RequestBodyLimit_RejectsUnknownLengthStreamingOverflow()
    {
        var configuration = new ConfigurationManager();
        configuration["Julgate:MAX_REQUEST_BODY_BYTES"] = "4";
        var middleware = new RequestBodyLimitMiddleware(async context =>
        {
            var buffer = new byte[16];
            while (await context.Request.Body.ReadAsync(buffer) > 0)
            {
            }
        }, configuration);
        var context = new DefaultHttpContext();
        context.Request.Method = HttpMethods.Post;
        context.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes("12345"));
        context.Response.Body = new MemoryStream();

        await middleware.InvokeAsync(context);

        Assert.Equal(StatusCodes.Status413PayloadTooLarge, context.Response.StatusCode);
    }
}
