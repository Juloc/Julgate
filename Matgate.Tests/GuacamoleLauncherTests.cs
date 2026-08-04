using System.Security.Cryptography;
using System.Text.Json;
using JulOS.Remote.Transport;
using Matgate.Models;
using Matgate.Services;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace Matgate.Tests;

public sealed class GuacamoleLauncherTests
{
    private const string JsonKeyHex = "00112233445566778899aabbccddeeff";

    [Fact]
    public async Task DesktopLaunchUsesSharedEncoderAndPreservesJulgateOptions()
    {
        var serverId = Guid.Parse("11111111-1111-4111-8111-111111111111");
        var server = new ServerEndpoint
        {
            Id = serverId,
            Name = "Desktop",
            Protocol = ServerProtocol.Rdp,
            Host = "host.example.test",
            Port = 3389,
            UserName = "remote-user",
            Password = "remote-password",
            Domain = "EXAMPLE",
            KeyboardLayout = "de-de-qwertz",
            IgnoreCertificate = true
        };
        var expectedConnectionName = GuacamoleConfigWriter.ConnectionName(server);
        var result = await CreateLauncher().CreateLaunchAsync(
            new MatgateUser { UserName = "operator" },
            server);

        Assert.True(result.Success);
        Assert.Null(result.Error);
        Assert.NotNull(result.Url);
        Assert.StartsWith("/guacamole/#/client/", result.Url, StringComparison.Ordinal);
        Assert.NotNull(result.EncryptedData);
        Assert.Equal(expectedConnectionName, result.ConnectionName);

        using var document = DecryptAndVerify(result.EncryptedData!);
        var root = document.RootElement;
        Assert.Equal("operator", root.GetProperty("username").GetString());
        var connection = root
            .GetProperty("connections")
            .GetProperty(expectedConnectionName);
        Assert.Equal(RemoteTransportProtocols.Rdp, connection.GetProperty("protocol").GetString());
        var parameters = connection.GetProperty("parameters");
        Assert.Equal("host.example.test", parameters.GetProperty("hostname").GetString());
        Assert.Equal("3389", parameters.GetProperty("port").GetString());
        Assert.Equal("remote-user", parameters.GetProperty("username").GetString());
        Assert.Equal("remote-password", parameters.GetProperty("password").GetString());
        Assert.Equal("EXAMPLE", parameters.GetProperty("domain").GetString());
        Assert.Equal("true", parameters.GetProperty("ignore-cert").GetString());
        Assert.Equal("Matgate", parameters.GetProperty("client-name").GetString());
        Assert.Equal("true", parameters.GetProperty("enable-drive").GetString());
        Assert.Equal("Matgate", parameters.GetProperty("drive-name").GetString());
        Assert.Equal($"/drive/{serverId:N}", parameters.GetProperty("drive-path").GetString());
    }

    [Fact]
    public async Task VncLaunchOmitsDesktopAndUserParameters()
    {
        var server = new ServerEndpoint
        {
            Name = "Console",
            Protocol = ServerProtocol.Vnc,
            Host = "console.example.test",
            Port = 5900,
            UserName = "ignored-user",
            Password = "vnc-password"
        };
        var expectedConnectionName = GuacamoleConfigWriter.ConnectionName(server);
        var result = await CreateLauncher().CreateLaunchAsync(
            new MatgateUser { UserName = "operator" },
            server);

        Assert.True(result.Success);
        Assert.Equal(expectedConnectionName, result.ConnectionName);
        using var document = DecryptAndVerify(result.EncryptedData!);
        var parameters = document.RootElement
            .GetProperty("connections")
            .GetProperty(expectedConnectionName)
            .GetProperty("parameters");
        Assert.False(parameters.TryGetProperty("username", out _));
        Assert.False(parameters.TryGetProperty("domain", out _));
        Assert.False(parameters.TryGetProperty("enable-drive", out _));
        Assert.Equal("vnc-password", parameters.GetProperty("password").GetString());
    }

    [Fact]
    public async Task InvalidSecretAndNonRemoteProtocolsFailWithoutLaunchData()
    {
        var invalidConfiguration = new ConfigurationManager();
        invalidConfiguration["Guacamole:JsonSecretKey"] = "invalid";
        var invalidLauncher = new GuacamoleLauncher(
            invalidConfiguration,
            new GuacamoleJsonLaunchEncoder());
        var invalidSecret = await invalidLauncher.CreateLaunchAsync(
            new MatgateUser { UserName = "operator" },
            new ServerEndpoint
            {
                Name = "Desktop",
                Protocol = ServerProtocol.Rdp,
                Host = "host.example.test",
                Port = 3389
            });
        var website = await CreateLauncher().CreateLaunchAsync(
            new MatgateUser { UserName = "operator" },
            new ServerEndpoint
            {
                Name = "Website",
                Protocol = ServerProtocol.Website,
                WebsiteUrl = "https://example.test"
            });

        Assert.False(invalidSecret.Success);
        Assert.Null(invalidSecret.EncryptedData);
        Assert.False(website.Success);
        Assert.Null(website.EncryptedData);
    }

    [Fact]
    public void EncoderComesFromThePublishedSharedAssembly()
    {
        Assert.Equal(
            "JulOS.Remote.Transport",
            typeof(GuacamoleJsonLaunchEncoder).Assembly.GetName().Name);
    }

    private static GuacamoleLauncher CreateLauncher()
    {
        var configuration = new ConfigurationManager();
        configuration["Guacamole:JsonSecretKey"] = JsonKeyHex;
        configuration["Guacamole:PublicBasePath"] = "/guacamole";
        configuration["Guacamole:DirectLaunch"] = "true";
        configuration["Guacamole:LaunchTtlMinutes"] = "2";
        return new GuacamoleLauncher(
            configuration,
            new GuacamoleJsonLaunchEncoder());
    }

    private static JsonDocument DecryptAndVerify(string encryptedData)
    {
        var key = Convert.FromHexString(JsonKeyHex);
        var encrypted = Convert.FromBase64String(encryptedData);
        Span<byte> initializationVector = stackalloc byte[16];
        byte[] decrypted;

        try
        {
            using var aes = Aes.Create();
            aes.Key = key;
#pragma warning disable CA5358 // This verifies the Guacamole-required JSON-auth format.
            decrypted = aes.DecryptCbc(
                encrypted,
                initializationVector,
                PaddingMode.PKCS7);
#pragma warning restore CA5358
        }
        finally
        {
            CryptographicOperations.ZeroMemory(key);
            CryptographicOperations.ZeroMemory(encrypted);
        }

        try
        {
            var signature = decrypted.AsSpan(0, 32);
            var payload = decrypted.AsSpan(32);
            var verificationKey = Convert.FromHexString(JsonKeyHex);
            var expected = HMACSHA256.HashData(verificationKey, payload);
            try
            {
                Assert.True(CryptographicOperations.FixedTimeEquals(signature, expected));
                var payloadBytes = payload.ToArray();
                try
                {
                    using var stream = new MemoryStream(payloadBytes, writable: false);
                    return JsonDocument.Parse(stream);
                }
                finally
                {
                    CryptographicOperations.ZeroMemory(payloadBytes);
                }
            }
            finally
            {
                CryptographicOperations.ZeroMemory(verificationKey);
                CryptographicOperations.ZeroMemory(expected);
            }
        }
        finally
        {
            CryptographicOperations.ZeroMemory(decrypted);
        }
    }
}
