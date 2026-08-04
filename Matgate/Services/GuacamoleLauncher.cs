using System.Security.Cryptography;
using System.Text;
using JulOS.Remote.Transport;
using Matgate.Models;

namespace Matgate.Services;

public sealed class GuacamoleLauncher
{
    private readonly IConfiguration _configuration;
    private readonly GuacamoleJsonLaunchEncoder _encoder;

    public GuacamoleLauncher(
        IConfiguration configuration,
        GuacamoleJsonLaunchEncoder encoder)
    {
        _configuration = configuration;
        _encoder = encoder;
    }

    public Task<GuacamoleLaunchResult> CreateLaunchAsync(
        MatgateUser user,
        ServerEndpoint server,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (ServerEndpoint.IsWebsiteProtocol(server.Protocol))
        {
            return Task.FromResult(GuacamoleLaunchResult.Failed(
                "Websites are opened directly in Matgate."));
        }

        if (!ServerEndpoint.IsGuacamoleProtocol(server.Protocol))
        {
            return Task.FromResult(GuacamoleLaunchResult.Failed(
                "Dateiverbindungen werden im Matgate-Dateimanager gestartet."));
        }

        var secret = _configuration["Guacamole:JsonSecretKey"]
            ?? Environment.GetEnvironmentVariable("GUACAMOLE_JSON_SECRET_KEY")
            ?? Environment.GetEnvironmentVariable("JSON_SECRET_KEY");

        if (!TryReadHexKey(secret, out var key))
        {
            return Task.FromResult(GuacamoleLaunchResult.Failed(
                "Guacamole JSON auth secret is missing or invalid. Set Guacamole:JsonSecretKey / GUACAMOLE_JSON_SECRET_KEY to 32 hex characters."));
        }

        byte[] passwordBytes = [];
        try
        {
            if (!string.IsNullOrWhiteSpace(server.Password))
            {
                passwordBytes = Encoding.UTF8.GetBytes(server.Password);
            }

            var connectionName = GuacamoleConfigWriter.ConnectionName(server);
            var sessionId = $"{server.Id:N}-{Guid.NewGuid():N}";
            var ttlMinutes = Math.Clamp(
                _configuration.GetValue("Guacamole:LaunchTtlMinutes", 2),
                1,
                30);
            var protocol = GuacamoleConfigWriter.ProtocolName(server.Protocol);
            var isDesktop = server.Protocol == ServerProtocol.Rdp;
            var request = new GuacamoleLaunchRequest(
                CallerName: user.UserName,
                ConnectionName: connectionName,
                SessionId: sessionId,
                Protocol: protocol,
                Host: server.Host,
                Port: server.Port,
                UserName: server.UserName,
                PasswordUtf8: passwordBytes,
                Domain: server.Domain,
                IgnoreCertificate: server.IgnoreCertificate,
                KeyboardLayout: server.KeyboardLayout,
                TerminalFontSize: server.TerminalFontSize,
                EnableDrive: isDesktop,
                DriveName: isDesktop ? "Matgate" : null,
                DrivePath: isDesktop ? $"/drive/{server.Id:N}" : null,
                ClientName: isDesktop ? "Matgate" : null,
                ExpiresAtUtc: DateTimeOffset.UtcNow.AddMinutes(ttlMinutes));
            var token = _encoder.Encode(request, key);
            var publicBasePath = _configuration["Guacamole:PublicBasePath"] ?? "/guacamole";
            var directLaunch = _configuration.GetValue("Guacamole:DirectLaunch", true);
            var url = directLaunch
                ? $"{publicBasePath.TrimEnd('/')}/#/client/{Uri.EscapeDataString(token.ClientIdentifier)}?data={Uri.EscapeDataString(token.EncryptedData)}"
                : $"{publicBasePath.TrimEnd('/')}/#/?data={Uri.EscapeDataString(token.EncryptedData)}";

            return Task.FromResult(GuacamoleLaunchResult.Ok(
                url,
                token.EncryptedData,
                token.ConnectionName));
        }
        catch (ArgumentException)
        {
            return Task.FromResult(GuacamoleLaunchResult.Failed(
                "The remote connection settings are invalid."));
        }
        finally
        {
            CryptographicOperations.ZeroMemory(key);
            CryptographicOperations.ZeroMemory(passwordBytes);
        }
    }

    private static bool TryReadHexKey(string? value, out byte[] key)
    {
        key = [];
        if (string.IsNullOrWhiteSpace(value) || value.Length != 32)
        {
            return false;
        }

        try
        {
            key = Convert.FromHexString(value);
            return key.Length == 16;
        }
        catch (FormatException)
        {
            return false;
        }
    }
}

public sealed record GuacamoleLaunchResult(
    bool Success,
    string? Url,
    string? Error,
    string? EncryptedData,
    string? ConnectionName)
{
    public static GuacamoleLaunchResult Ok(string url, string encryptedData, string connectionName)
    {
        return new(true, url, null, encryptedData, connectionName);
    }

    public static GuacamoleLaunchResult Failed(string error) => new(false, null, error, null, null);
}
