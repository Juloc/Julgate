using Matgate.Web;
using Microsoft.AspNetCore.Http;
using Xunit;

namespace Matgate.Tests;

public sealed class JulgateShellCompatibilityTests
{
    [Theory]
    [InlineData("/workspaces")]
    [InlineData("/workspaces/new")]
    [InlineData("/admin/servers")]
    [InlineData("/admin/users")]
    [InlineData("/tools")]
    [InlineData("/account")]
    [InlineData("/about")]
    public void EmbeddedShellPages_AreRestrictedToExplicitSameOriginRoutes(string path)
    {
        var context = new DefaultHttpContext();
        context.Request.Method = HttpMethods.Get;
        context.Request.Path = path;
        context.Request.QueryString = new QueryString("?embed=1");

        Assert.True(JulgateShellCompatibilityMiddleware.IsSafeEmbeddedPage(context.Request));
    }

    [Theory]
    [InlineData("/")]
    [InlineData("/login")]
    [InlineData("/api/connections/00000000-0000-0000-0000-000000000000/launch")]
    [InlineData("/website/demo")]
    public void UnapprovedPages_CannotOptIntoFraming(string path)
    {
        var context = new DefaultHttpContext();
        context.Request.Method = HttpMethods.Get;
        context.Request.Path = path;
        context.Request.QueryString = new QueryString("?embed=1");

        Assert.False(JulgateShellCompatibilityMiddleware.IsSafeEmbeddedPage(context.Request));
    }

    [Fact]
    public void JulgateCsrfHeader_IsNormalizedForLegacyEndpointValidation()
    {
        var context = new DefaultHttpContext();
        context.Request.Headers[JulgateShellCompatibilityMiddleware.JulgateCsrfHeader] = "token-value";

        JulgateShellCompatibilityMiddleware.NormalizeCsrfHeader(context.Request);

        Assert.Equal(
            "token-value",
            context.Request.Headers[JulgateShellCompatibilityMiddleware.LegacyCsrfHeader].ToString());
    }

    [Fact]
    public void ExistingLegacyCsrfHeader_IsNeverOverwritten()
    {
        var context = new DefaultHttpContext();
        context.Request.Headers[JulgateShellCompatibilityMiddleware.JulgateCsrfHeader] = "new-token";
        context.Request.Headers[JulgateShellCompatibilityMiddleware.LegacyCsrfHeader] = "legacy-token";

        JulgateShellCompatibilityMiddleware.NormalizeCsrfHeader(context.Request);

        Assert.Equal(
            "legacy-token",
            context.Request.Headers[JulgateShellCompatibilityMiddleware.LegacyCsrfHeader].ToString());
    }
}
