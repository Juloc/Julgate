using Matgate.Models;
using Matgate.Services;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace Matgate.Tests;

public sealed class ConnectionLaunchTests
{
    [Theory]
    [InlineData(ServerProtocol.Rdp, 3389)]
    [InlineData(ServerProtocol.Vnc, 5900)]
    [InlineData(ServerProtocol.Ssh, 22)]
    public async Task GuacamoleProtocols_CreateEncryptedShortLivedLaunches(ServerProtocol protocol, int port)
    {
        var configuration = new ConfigurationManager();
        configuration["Guacamole:JsonSecretKey"] = "fedcba98765432100123456789abcdef";
        configuration["Guacamole:PublicBasePath"] = "/guacamole";
        configuration["Guacamole:DirectLaunch"] = "true";
        configuration["Guacamole:LaunchTtlMinutes"] = "1";
        var launcher = new GuacamoleLauncher(configuration);
        var server = new ServerEndpoint
        {
            Id = Guid.NewGuid(),
            Name = $"Test {protocol}",
            Protocol = protocol,
            Host = "192.168.10.20",
            Port = port,
            UserName = "target-user",
            Password = "target-password",
            IsEnabled = true
        };
        var user = new MatgateUser
        {
            Id = Guid.NewGuid(),
            UserName = "tester",
            IsEnabled = true
        };

        var result = await launcher.CreateLaunchAsync(user, server);

        Assert.True(result.Success, result.Error);
        Assert.NotNull(result.Url);
        Assert.StartsWith("/guacamole/#/client/", result.Url, StringComparison.Ordinal);
        Assert.NotNull(result.EncryptedData);
        Assert.DoesNotContain(server.Password, result.EncryptedData!, StringComparison.Ordinal);
        Assert.DoesNotContain(server.Host, result.EncryptedData!, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(ServerProtocol.Sftp)]
    [InlineData(ServerProtocol.Ftp)]
    [InlineData(ServerProtocol.Smb)]
    [InlineData(ServerProtocol.Website)]
    public async Task NonGuacamoleProtocols_CannotCreateGuacamoleLaunches(ServerProtocol protocol)
    {
        var configuration = new ConfigurationManager();
        configuration["Guacamole:JsonSecretKey"] = "fedcba98765432100123456789abcdef";
        var launcher = new GuacamoleLauncher(configuration);
        var result = await launcher.CreateLaunchAsync(
            new MatgateUser { UserName = "tester", IsEnabled = true },
            new ServerEndpoint { Protocol = protocol, Host = "192.168.10.20", Port = 1 });

        Assert.False(result.Success);
        Assert.Null(result.Url);
    }
}
