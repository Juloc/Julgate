using Matgate.Services;
using Xunit;

namespace Matgate.Tests;

public sealed class AccountPasswordChangeIntegrationTests
{
    [Fact]
    public void AccountPasswordChange_IsIntegratedWithCurrentSecurityBoundaries()
    {
        var repositoryRoot = Path.GetFullPath(
            Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));
        var endpointSource = File.ReadAllText(
            Path.Combine(repositoryRoot, "Matgate", "Web", "EndpointMapping.cs"));
        var viewSource = File.ReadAllText(
            Path.Combine(repositoryRoot, "Matgate", "Web", "HtmlViews.cs"));

        Assert.Contains("/account/password", endpointSource, StringComparison.Ordinal);
        Assert.Contains("ValidateCsrf(context, form)", endpointSource, StringComparison.Ordinal);
        Assert.Contains("hasher.Verify(currentPassword, user.PasswordHash)", endpointSource, StringComparison.Ordinal);
        Assert.Contains("newPassword.Length < 10", endpointSource, StringComparison.Ordinal);
        Assert.Contains("stored.PasswordHash = hasher.Hash(newPassword)", endpointSource, StringComparison.Ordinal);
        Assert.Contains("/account/password", viewSource, StringComparison.Ordinal);
        Assert.Contains("current-password", viewSource, StringComparison.Ordinal);
        Assert.Contains("new-password", viewSource, StringComparison.Ordinal);
    }

    [Fact]
    public void PasswordHasher_VerifiesOnlyTheNewPasswordHash()
    {
        var hasher = new PasswordHasher();
        var hash = hasher.Hash("new-secure-password");

        Assert.True(hasher.Verify("new-secure-password", hash));
        Assert.False(hasher.Verify("old-secure-password", hash));
    }
}
