using Matgate.Models;

namespace Matgate.Services;

public sealed class GuacamoleConfigWriter
{
    private readonly JsonDataStore _store;
    private readonly IConfiguration _configuration;
    private readonly ILogger<GuacamoleConfigWriter> _logger;

    public GuacamoleConfigWriter(
        JsonDataStore store,
        IConfiguration configuration,
        ILogger<GuacamoleConfigWriter> logger)
    {
        _store = store;
        _configuration = configuration;
        _logger = logger;
    }

    public Task SynchronizeAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var directories = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            _store.DataDirectory,
            Path.Combine(_store.DataDirectory, "guacamole")
        };

        var configuredDirectory = Environment.GetEnvironmentVariable("JULGATE_GUACAMOLE_CONFIG_DIR")
            ?? _configuration["Guacamole:ConfigDirectory"];
        if (!string.IsNullOrWhiteSpace(configuredDirectory))
        {
            directories.Add(Path.GetFullPath(configuredDirectory));
        }

        var removed = 0;
        foreach (var directory in directories)
        {
            removed += DeleteIfPresent(Path.Combine(directory, "user-mapping.xml"));
            removed += DeleteIfPresent(Path.Combine(directory, "user-mapping.xml.tmp"));
            removed += DeleteIfPresent(Path.Combine(directory, "guacamole.properties"));
            removed += DeleteIfPresent(Path.Combine(directory, "guacamole.properties.tmp"));
        }

        if (removed > 0)
        {
            _logger.LogWarning(
                "Removed {FileCount} legacy Guacamole file-auth configuration file(s). Julgate now supplies connection credentials only through short-lived encrypted JSON launch tokens.",
                removed);
        }
        else
        {
            _logger.LogInformation(
                "Guacamole JSON authentication is active; no persistent plaintext connection mapping is written.");
        }

        return Task.CompletedTask;
    }

    public static string ConnectionName(ServerEndpoint server)
    {
        return $"{server.Name} [{server.Id.ToString("N")[..8]}]";
    }

    public static string ProtocolName(ServerProtocol protocol)
    {
        return protocol.ToString().ToLowerInvariant();
    }

    private static int DeleteIfPresent(string path)
    {
        try
        {
            if (!File.Exists(path))
            {
                return 0;
            }

            File.Delete(path);
            return 1;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            throw new InvalidOperationException(
                $"Legacy Guacamole credential file '{path}' could not be removed safely.",
                exception);
        }
    }
}
