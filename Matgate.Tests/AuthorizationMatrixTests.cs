using System.Reflection;
using Matgate.Models;
using Matgate.Web;
using Xunit;

namespace Matgate.Tests;

public sealed class AuthorizationMatrixTests
{
    private static readonly MethodInfo CanAccessMethod = typeof(EndpointMapping)
        .GetMethod("CanAccessServer", BindingFlags.NonPublic | BindingFlags.Static)
        ?? throw new InvalidOperationException("CanAccessServer was not found.");

    private static readonly MethodInfo CanEditMethod = typeof(EndpointMapping)
        .GetMethod("CanEditServer", BindingFlags.NonPublic | BindingFlags.Static)
        ?? throw new InvalidOperationException("CanEditServer was not found.");

    [Fact]
    public void ServerPermissions_FollowTheCompleteRoleAndOwnershipMatrix()
    {
        var ownerId = Guid.NewGuid();
        var otherId = Guid.NewGuid();
        var globalServer = new ServerEndpoint { Id = Guid.NewGuid(), OwnerUserId = null };
        var ownedServer = new ServerEndpoint { Id = Guid.NewGuid(), OwnerUserId = ownerId };

        var admin = User(Guid.NewGuid(), isAdmin: true);
        AssertPermission(admin, globalServer, canAccess: true, canEdit: true);
        AssertPermission(admin, ownedServer, canAccess: true, canEdit: true);

        var owner = User(ownerId);
        AssertPermission(owner, ownedServer, canAccess: true, canEdit: true);

        var unrelated = User(otherId);
        AssertPermission(unrelated, ownedServer, canAccess: false, canEdit: false);
        AssertPermission(unrelated, globalServer, canAccess: false, canEdit: false);

        var manager = User(Guid.NewGuid(), canManageServers: true);
        AssertPermission(manager, globalServer, canAccess: true, canEdit: true);
        AssertPermission(manager, ownedServer, canAccess: false, canEdit: false);

        var allGlobal = User(Guid.NewGuid(), serverAccessAll: true);
        AssertPermission(allGlobal, globalServer, canAccess: true, canEdit: false);
        AssertPermission(allGlobal, ownedServer, canAccess: false, canEdit: false);

        var explicitlyGranted = User(Guid.NewGuid(), grants: [globalServer.Id]);
        AssertPermission(explicitlyGranted, globalServer, canAccess: true, canEdit: false);
        AssertPermission(explicitlyGranted, ownedServer, canAccess: false, canEdit: false);
    }

    private static MatgateUser User(
        Guid id,
        bool isAdmin = false,
        bool canManageServers = false,
        bool serverAccessAll = false,
        IReadOnlyList<Guid>? grants = null)
    {
        return new MatgateUser
        {
            Id = id,
            IsAdmin = isAdmin,
            CanManageServers = canManageServers,
            ServerAccessAll = serverAccessAll,
            ServerAccess = grants?.ToList() ?? []
        };
    }

    private static void AssertPermission(
        MatgateUser user,
        ServerEndpoint server,
        bool canAccess,
        bool canEdit)
    {
        Assert.Equal(canAccess, (bool)CanAccessMethod.Invoke(null, [user, server])!);
        Assert.Equal(canEdit, (bool)CanEditMethod.Invoke(null, [user, server])!);
    }
}
