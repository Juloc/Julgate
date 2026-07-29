using System.Security.Cryptography;
using Matgate.Models;
using Matgate.Services;
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
}
