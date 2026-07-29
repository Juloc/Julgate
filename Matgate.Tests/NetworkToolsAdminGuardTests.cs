using System.Security.Claims;
using Matgate.Web;
using Microsoft.AspNetCore.Http;
using Xunit;

namespace Matgate.Tests;

public sealed class NetworkToolsAdminGuardTests
{
    [Theory]
    [InlineData("/tools")]
    [InlineData("/tools/")]
    [InlineData("/api/tools/ping")]
    [InlineData("/api/tools/ports")]
    public void NetworkToolRoutes_RequireAdministrator(string path)
    {
        Assert.True(NetworkToolsAdminGuardMiddleware.RequiresAdministrator(new PathString(path)));
    }

    [Theory]
    [InlineData("/")]
    [InlineData("/sessions")]
    [InlineData("/api/files/server/list")]
    public void OtherRoutes_DoNotUseNetworkToolsGuard(string path)
    {
        Assert.False(NetworkToolsAdminGuardMiddleware.RequiresAdministrator(new PathString(path)));
    }

    [Theory]
    [InlineData("admin")]
    [InlineData("Admin")]
    [InlineData("ADMIN")]
    public void AdministratorRole_IsMatchedCaseInsensitively(string role)
    {
        var principal = new ClaimsPrincipal(
            new ClaimsIdentity([new Claim(ClaimTypes.Role, role)], "test"));

        Assert.True(NetworkToolsAdminGuardMiddleware.IsAdministrator(principal));
    }

    [Fact]
    public void NonAdministratorRole_IsRejected()
    {
        var principal = new ClaimsPrincipal(
            new ClaimsIdentity([new Claim(ClaimTypes.Role, "user")], "test"));

        Assert.False(NetworkToolsAdminGuardMiddleware.IsAdministrator(principal));
    }
}
