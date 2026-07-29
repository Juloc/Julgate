using System.Net;
using Matgate.Web;
using Xunit;

namespace Matgate.Tests;

public sealed class WebsiteProxyTargetGuardTests
{
    [Theory]
    [InlineData("0.0.0.0")]
    [InlineData("127.0.0.1")]
    [InlineData("169.254.169.254")]
    [InlineData("100.100.100.200")]
    [InlineData("168.63.129.16")]
    [InlineData("224.0.0.1")]
    [InlineData("fe80::1")]
    [InlineData("fd00:ec2::254")]
    public void NonRoutableAndMetadataAddresses_AreBlocked(string value)
    {
        Assert.True(WebsiteProxyTargetGuardMiddleware.IsDisallowedAddress(IPAddress.Parse(value)));
    }

    [Theory]
    [InlineData("10.0.0.5")]
    [InlineData("172.16.0.5")]
    [InlineData("192.168.1.10")]
    public void PrivateHomeNetworkAddresses_AreAllowed(string value)
    {
        Assert.False(WebsiteProxyTargetGuardMiddleware.IsDisallowedAddress(IPAddress.Parse(value)));
    }
}
