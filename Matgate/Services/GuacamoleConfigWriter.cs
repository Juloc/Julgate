using System.Security.Cryptography;
using System.Text;
using System.Xml.Linq;
using Matgate.Models;

namespace Matgate.Services;

public sealed class GuacamoleConfigWriter
{
    private readonly JsonDataStore _store;
    private readonly IConfiguration _configuration;
    private readonly ILogger<GuacamoleConfigWriter> _logger;

    public GuacamoleConfigWriter(JsonDataStore store, IConfiguration configuration, ILogger<GuacamoleConfigWriter> logger)
    {
        _store = store;
        _configuration = configuration;
        _logger = logger;
    }

    public async Task SynchronizeAsync(CancellationToken cancellationToken = default)
    {
        var users = await _store.GetUsersAsync(cancellationToken);
        var servers = await _store.GetServersAsync(cancellationToken);
        var enabledServers = servers
            .Where(server => server.IsEnabled && ServerEndpoint.IsGuacamoleProtocol(server.Protocol))
            .ToList();

        var configHome = _store.DataDirectory;
        Directory.CreateDirectory(configHome);

        await WritePropertiesAsync(configHome, cancellationToken);
        await WriteUserMappingAsync(configHome, users, enabledServers, cancellationToken);

        _logger.LogInformation(
            "Synchronized Guacamole config with {UserCount} user(s) and {ServerCount} server(s).",
            users.Count,
            enabledServers.Count);
    }

    public static string ConnectionName(ServerEndpoint server)
    {
        return $"{server.Name} [{server.Id.ToString("N")[..8]}]";
    }

    public static string ProtocolName(ServerProtocol protocol)
    {
        return protocol.ToString().ToLowerInvariant();
    }

    private async Task WritePropertiesAsync(string configHome, CancellationToken cancellationToken)
    {
        var guacdHost = _configuration["Guacamole:GuacdHost"]
            ?? Environment.GetEnvironmentVariable("GUACD_HOSTNAME")
            ?? "guacd";
        var guacdPort = _configuration["Guacamole:GuacdPort"]
            ?? Environment.GetEnvironmentVariable("GUACD_PORT")
            ?? "4822";

        var content = string.Join('\n',
            $"guacd-hostname: {guacdHost}",
            $"guacd-port: {guacdPort}",
            "user-mapping: /etc/guacamole/user-mapping.xml",
            "enable-websocket: true",
            "");

        await File.WriteAllTextAsync(Path.Combine(configHome, "guacamole.properties"), content, cancellationToken);
    }

    private static async Task WriteUserMappingAsync(
        string configHome,
        IReadOnlyList<MatgateUser> users,
        IReadOnlyList<ServerEndpoint> servers,
        CancellationToken cancellationToken)
    {
        var root = new XElement("user-mapping");

        // Connections are launched via self-contained JSON-auth tokens (GuacamoleLauncher), so the
        // file-auth provider is only a formality here. We deliberately do NOT write device hostnames,
        // usernames or passwords into this file (that was a second cleartext copy of every credential);
        // and the per-user login secret is stored hashed, not in cleartext.
        foreach (var user in users.Where(user => user.IsEnabled).OrderBy(user => user.UserName))
        {
            root.Add(new XElement(
                "authorize",
                new XAttribute("username", user.UserName),
                new XAttribute("password", Sha256Hex(user.GuacamolePassword)),
                new XAttribute("encoding", "sha256")));
        }

        var document = new XDocument(new XDeclaration("1.0", "utf-8", null), root);
        await File.WriteAllTextAsync(Path.Combine(configHome, "user-mapping.xml"), document.ToString(), cancellationToken);
    }

    private static string Sha256Hex(string? value)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(value ?? ""));
        return Convert.ToHexString(hash);
    }
}
