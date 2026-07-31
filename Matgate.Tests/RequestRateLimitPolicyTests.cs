using System.Net;
using System.Security.Claims;
using Matgate.Web;
using Microsoft.AspNetCore.Http;
using Xunit;

namespace Matgate.Tests;

public sealed class RequestRateLimitPolicyTests
{
    [Fact]
    public void LoginAttemptsRemainStrictlyLimitedByClientAddress()
    {
        var context = CreateContext(HttpMethods.Post, "/login", "192.168.1.20");

        var decision = RequestRateLimitPolicy.Classify(context, 10, 120, 1200);

        Assert.Equal(RequestRateLimitBucket.Login, decision.Bucket);
        Assert.Equal("login:ip:192.168.1.20", decision.PartitionKey);
        Assert.Equal(10, decision.PermitLimit);
    }

    [Fact]
    public void AuthenticatedReadsUseAHighPerUserLimit()
    {
        var userId = Guid.NewGuid();
        var context = CreateContext(HttpMethods.Get, "/", "192.168.1.20", userId);

        var decision = RequestRateLimitPolicy.Classify(context, 10, 120, 1200);

        Assert.Equal(RequestRateLimitBucket.Read, decision.Bucket);
        Assert.Equal($"read:user:{userId}", decision.PartitionKey);
        Assert.Equal(1200, decision.PermitLimit);
    }

    [Fact]
    public void AuthenticatedWritesUseASeparatePerUserLimit()
    {
        var userId = Guid.NewGuid();
        var context = CreateContext(HttpMethods.Post, "/api/connections/test/launch", "192.168.1.20", userId);

        var decision = RequestRateLimitPolicy.Classify(context, 10, 120, 1200);

        Assert.Equal(RequestRateLimitBucket.Write, decision.Bucket);
        Assert.Equal($"write:user:{userId}", decision.PartitionKey);
        Assert.Equal(120, decision.PermitLimit);
    }

    [Fact]
    public void UsersBehindTheSameReverseProxyDoNotShareTheReadBucket()
    {
        var firstUser = Guid.NewGuid();
        var secondUser = Guid.NewGuid();
        var first = CreateContext(HttpMethods.Get, "/", "172.18.0.2", firstUser);
        var second = CreateContext(HttpMethods.Get, "/", "172.18.0.2", secondUser);

        var firstDecision = RequestRateLimitPolicy.Classify(first, 10, 120, 1200);
        var secondDecision = RequestRateLimitPolicy.Classify(second, 10, 120, 1200);

        Assert.NotEqual(firstDecision.PartitionKey, secondDecision.PartitionKey);
    }

    private static DefaultHttpContext CreateContext(
        string method,
        string path,
        string remoteAddress,
        Guid? userId = null)
    {
        var context = new DefaultHttpContext();
        context.Request.Method = method;
        context.Request.Path = path;
        context.Connection.RemoteIpAddress = IPAddress.Parse(remoteAddress);

        if (userId is not null)
        {
            context.User = new ClaimsPrincipal(new ClaimsIdentity(
            [
                new Claim(ClaimTypes.NameIdentifier, userId.Value.ToString())
            ],
            authenticationType: "Test"));
        }

        return context;
    }
}
