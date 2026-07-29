using System.Security.Cryptography;
using Matgate.Models;
using Matgate.Services;
using Matgate.Web;
using Xunit;

namespace Matgate.Tests;

public sealed class SecurityTests
{
    [Fact]
    public void CredentialProtector_RoundTripsAndDoesNotExposePlaintext()
    {
        var key = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
        using var protector = new CredentialProtector(key);

        var protectedValue = protector.Protect("server-password");

        Assert.True(protectedValue.StartsWith("julgate-aesgcm:v1:", StringComparison.Ordinal));
        Assert.False(protectedValue.Contains("server-password", StringComparison.Ordinal));
        Assert.Equal("server-password", protector.Unprotect(protectedValue));
        Assert.Equal(protectedValue, protector.Protect(protectedValue));
    }

    [Fact]
    public void CredentialProtector_RejectsWrongKey()
    {
        using var first = new CredentialProtector(Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)));
        using var second = new CredentialProtector(Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)));
        var protectedValue = first.Protect("server-password");

        Assert.Throws<InvalidOperationException>(() => second.Unprotect(protectedValue));
    }

    [Fact]
    public void PasswordHasher_UsesSaltAndVerifiesCorrectPassword()
    {
        var hasher = new PasswordHasher();

        var first = hasher.Hash("correct horse battery staple");
        var second = hasher.Hash("correct horse battery staple");

        Assert.NotEqual(first, second);
        Assert.True(hasher.Verify("correct horse battery staple", first));
        Assert.False(hasher.Verify("wrong password", first));
    }

    [Fact]
    public void ServerEndpoint_VerifiesCertificatesByDefault()
    {
        var endpoint = new ServerEndpoint();

        Assert.False(endpoint.IgnoreCertificate);
    }

    [Theory]
    [InlineData("file:///etc/passwd")]
    [InlineData("https://user:password@server.home/")]
    [InlineData("http://169.254.169.254/latest/meta-data/")]
    [InlineData("http://metadata.google.internal/")]
    public void WebsiteProxy_RejectsUnsafeTargets(string target)
    {
        Assert.Equal("", ServerEndpoint.NormalizeWebsiteUrl(target));
    }

    [Fact]
    public void WebsiteProxy_AllowsPrivateHomeNetworkTargets()
    {
        Assert.Equal(
            "https://192.168.1.10/",
            ServerEndpoint.NormalizeWebsiteUrl("https://192.168.1.10"));
    }

    [Theory]
    [InlineData("../secret")]
    [InlineData("folder/../../secret")]
    [InlineData("folder%2f..%2fsecret")]
    [InlineData("folder%252f..%252fsecret")]
    [InlineData("folder\\..\\secret")]
    public void PathGuard_RejectsTraversalVariants(string value)
    {
        Assert.True(PathTraversalGuardMiddleware.ContainsUnsafePath(value));
    }

    [Theory]
    [InlineData("/")]
    [InlineData("/folder/file.txt")]
    [InlineData("folder-name")]
    public void PathGuard_AllowsNormalPaths(string value)
    {
        Assert.False(PathTraversalGuardMiddleware.ContainsUnsafePath(value));
    }

    [Theory]
    [InlineData("../file.txt")]
    [InlineData("folder/file.txt")]
    [InlineData("..\\file.txt")]
    public void PathGuard_RejectsUnsafeUploadNames(string value)
    {
        Assert.True(PathTraversalGuardMiddleware.IsUnsafeLeafName(value));
    }

    [Fact]
    public void WorkspaceCookie_IsStrictHttpOnlyAndSecureOnHttps()
    {
        var rewritten = WorkspaceCookieHardeningMiddleware.Rewrite(
            "Matgate.Workspace.Access.example=value; path=/; samesite=lax; httponly",
            isHttps: true);

        Assert.Contains("SameSite=Strict", rewritten, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("HttpOnly", rewritten, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Secure", rewritten, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void UnrelatedCookie_RemainsUnchanged()
    {
        const string cookie = "Other.Cookie=value; path=/; samesite=lax";

        Assert.Equal(cookie, WorkspaceCookieHardeningMiddleware.Rewrite(cookie, isHttps: true));
    }
}
