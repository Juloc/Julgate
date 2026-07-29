using Matgate.Models;
using Matgate.Services;
using Microsoft.AspNetCore.DataProtection;

namespace Matgate.Tests;

public sealed class SecurityTests
{
    [Fact]
    public void CredentialProtector_RoundTripsAndDoesNotExposePlaintext()
    {
        var provider = new EphemeralDataProtectionProvider();
        var protector = new CredentialProtector(provider);

        var protectedValue = protector.Protect("server-password");

        Assert.True(protectedValue.StartsWith("julgate-protected:v1:", StringComparison.Ordinal));
        Assert.False(protectedValue.Contains("server-password", StringComparison.Ordinal));
        Assert.Equal("server-password", protector.Unprotect(protectedValue));
        Assert.Equal(protectedValue, protector.Protect(protectedValue));
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
