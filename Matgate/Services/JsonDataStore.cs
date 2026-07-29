using System.Text.Json;
using Matgate.Models;

namespace Matgate.Services;

public sealed class JsonDataStore
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly ILogger<JsonDataStore> _logger;

    public JsonDataStore(IConfiguration configuration, IHostEnvironment environment, ILogger<JsonDataStore> logger)
    {
        _logger = logger;

        var configured = FirstEnvironmentValue("JULGATE_DATA_DIR", "MATGATE_DATA_DIR")
            ?? configuration["Matgate:DataDirectory"];
        var configuredWorkspaceRoot = FirstEnvironmentValue("JULGATE_WORKSPACE_ROOT", "MATGATE_WORKSPACE_ROOT")
            ?? configuration["Matgate:WorkspaceRoot"];

        DataDirectory = Path.GetFullPath(string.IsNullOrWhiteSpace(configured)
            ? Path.Combine(environment.ContentRootPath, "data")
            : configured);
        WorkspaceRootDirectory = Path.GetFullPath(string.IsNullOrWhiteSpace(configuredWorkspaceRoot)
            ? Path.Combine(DataDirectory, "workspaces")
            : configuredWorkspaceRoot);

        Directory.CreateDirectory(DataDirectory);
        Directory.CreateDirectory(WorkspaceRootDirectory);
        SetPrivateDirectoryPermissions(DataDirectory);
        SetPrivateDirectoryPermissions(WorkspaceRootDirectory);
    }

    public string DataDirectory { get; }

    public string WorkspaceRootDirectory { get; }

    private string UsersPath => Path.Combine(DataDirectory, "users.json");

    private string ServersPath => Path.Combine(DataDirectory, "servers.json");

    private string WorkspacesPath => Path.Combine(DataDirectory, "workspaces.json");

    public async Task<IReadOnlyList<MatgateUser>> GetUsersAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            return await ReadListAsync<MatgateUser>(UsersPath, cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<IReadOnlyList<ServerEndpoint>> GetServersAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            return await ReadListAsync<ServerEndpoint>(ServersPath, cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<IReadOnlyList<WorkspaceDefinition>> GetWorkspacesAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            return await ReadListAsync<WorkspaceDefinition>(WorkspacesPath, cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task EnsureWorkspacePublicAccessDefaultsAsync(TimeSpan defaultDuration, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var workspaces = await ReadListAsync<WorkspaceDefinition>(WorkspacesPath, cancellationToken);
            var now = DateTimeOffset.UtcNow;
            var changed = false;

            foreach (var workspace in workspaces.Where(workspace => workspace.PublicAccessExpiresAt is null))
            {
                workspace.PublicAccessExpiresAt = now.Add(defaultDuration);
                workspace.UpdatedAt = now;
                changed = true;
            }

            if (changed)
            {
                await WriteListAsync(WorkspacesPath, workspaces, cancellationToken);
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<MatgateUser?> FindUserByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        return (await GetUsersAsync(cancellationToken)).FirstOrDefault(user => user.Id == id);
    }

    public async Task<MatgateUser?> FindUserByNameAsync(string userName, CancellationToken cancellationToken = default)
    {
        var normalized = PasswordHasher.NormalizeUserName(userName);
        return (await GetUsersAsync(cancellationToken))
            .FirstOrDefault(user => string.Equals(user.UserName, normalized, StringComparison.OrdinalIgnoreCase));
    }

    public async Task<ServerEndpoint?> FindServerByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        return (await GetServersAsync(cancellationToken)).FirstOrDefault(server => server.Id == id);
    }

    public async Task<WorkspaceDefinition?> FindWorkspaceByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        return (await GetWorkspacesAsync(cancellationToken)).FirstOrDefault(workspace => workspace.Id == id);
    }

    public async Task UpdateUsersAsync(Action<List<MatgateUser>> update, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var users = await ReadListAsync<MatgateUser>(UsersPath, cancellationToken);
            update(users);
            await WriteListAsync(UsersPath, users, cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task UpdateServersAsync(Action<List<ServerEndpoint>> update, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var servers = await ReadListAsync<ServerEndpoint>(ServersPath, cancellationToken);
            update(servers);
            await WriteListAsync(ServersPath, servers, cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task UpdateWorkspacesAsync(Action<List<WorkspaceDefinition>> update, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var workspaces = await ReadListAsync<WorkspaceDefinition>(WorkspacesPath, cancellationToken);
            update(workspaces);
            await WriteListAsync(WorkspacesPath, workspaces, cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task EnsureSeedAdminAsync(PasswordHasher hasher, ILogger logger, CancellationToken cancellationToken = default)
    {
        var users = await GetUsersAsync(cancellationToken);
        if (users.Count > 0)
        {
            return;
        }

        var userName = PasswordHasher.NormalizeUserName(
            FirstEnvironmentValue("JULGATE_ADMIN_USER", "MATGATE_ADMIN_USER") ?? "admin");
        var password = FirstEnvironmentValue("JULGATE_ADMIN_PASSWORD", "MATGATE_ADMIN_PASSWORD");

        if (!PasswordHasher.IsValidUserName(userName))
        {
            throw new InvalidOperationException("JULGATE_ADMIN_USER must contain 3 to 64 safe characters.");
        }

        if (string.IsNullOrWhiteSpace(password)
            || password.Length < 16
            || string.Equals(password, "change-me-now", StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "The first start requires JULGATE_ADMIN_PASSWORD with at least 16 characters. Default passwords are rejected.");
        }

        await UpdateUsersAsync(current =>
        {
            if (current.Count > 0)
            {
                return;
            }

            var now = DateTimeOffset.UtcNow;
            current.Add(new MatgateUser
            {
                UserName = userName,
                DisplayName = "Administrator",
                PasswordHash = hasher.Hash(password),
                GuacamolePassword = hasher.GenerateSecret(),
                IsAdmin = true,
                CanManageServers = true,
                CanCreateServers = true,
                PreferredLanguage = "en",
                IsEnabled = true,
                CreatedAt = now,
                UpdatedAt = now
            });
        }, cancellationToken);

        logger.LogInformation("Initial Julgate administrator '{UserName}' was created.", userName);
    }

    public async Task EnsureGuacamoleSecretsAsync(PasswordHasher hasher, CancellationToken cancellationToken = default)
    {
        var changed = false;
        await UpdateUsersAsync(users =>
        {
            foreach (var user in users.Where(user => string.IsNullOrWhiteSpace(user.GuacamolePassword)))
            {
                user.GuacamolePassword = hasher.GenerateSecret();
                user.UpdatedAt = DateTimeOffset.UtcNow;
                changed = true;
            }
        }, cancellationToken);

        if (changed)
        {
            _logger.LogInformation("Generated missing Guacamole bridge passwords for existing users.");
        }
    }

    public async Task RemoveLegacyGatewayServersAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var servers = await ReadListAsync<ServerEndpoint>(ServersPath, cancellationToken);
            var legacyServerIds = servers
                .Where(server => server.Protocol == ServerProtocol.LegacyBrowser)
                .Select(server => server.Id)
                .ToHashSet();

            if (legacyServerIds.Count == 0)
            {
                return;
            }

            servers.RemoveAll(server => server.Protocol == ServerProtocol.LegacyBrowser);
            await WriteListAsync(ServersPath, servers, cancellationToken);

            var users = await ReadListAsync<MatgateUser>(UsersPath, cancellationToken);
            foreach (var user in users)
            {
                user.FavoriteServerIds ??= [];
                user.ServerAccess.RemoveAll(legacyServerIds.Contains);
                user.FavoriteServerIds.RemoveAll(legacyServerIds.Contains);
            }

            await WriteListAsync(UsersPath, users, cancellationToken);
            _logger.LogInformation("Removed legacy gateway server entries from the data store.");
        }
        finally
        {
            _gate.Release();
        }
    }

    private static async Task<List<T>> ReadListAsync<T>(string path, CancellationToken cancellationToken)
    {
        if (!File.Exists(path))
        {
            return [];
        }

        SetPrivateFilePermissions(path);
        await using var stream = File.OpenRead(path);
        return await JsonSerializer.DeserializeAsync<List<T>>(stream, JsonOptions, cancellationToken) ?? [];
    }

    private static async Task WriteListAsync<T>(string path, List<T> values, CancellationToken cancellationToken)
    {
        var directory = Path.GetDirectoryName(path)!;
        Directory.CreateDirectory(directory);
        SetPrivateDirectoryPermissions(directory);

        var tempPath = path + ".tmp";
        var backupPath = path + ".bak";

        await using (var stream = new FileStream(
                         tempPath,
                         FileMode.Create,
                         FileAccess.Write,
                         FileShare.None,
                         64 * 1024,
                         FileOptions.Asynchronous | FileOptions.SequentialScan))
        {
            await JsonSerializer.SerializeAsync(stream, values, JsonOptions, cancellationToken);
            await stream.FlushAsync(cancellationToken);
        }

        SetPrivateFilePermissions(tempPath);

        if (File.Exists(path))
        {
            File.Copy(path, backupPath, overwrite: true);
            SetPrivateFilePermissions(backupPath);
        }

        File.Move(tempPath, path, overwrite: true);
        SetPrivateFilePermissions(path);
    }

    private static string? FirstEnvironmentValue(params string[] names)
    {
        foreach (var name in names)
        {
            var value = Environment.GetEnvironmentVariable(name);
            if (!string.IsNullOrWhiteSpace(value))
            {
                return value.Trim();
            }
        }

        return null;
    }

    private static void SetPrivateDirectoryPermissions(string path)
    {
        try
        {
            File.SetUnixFileMode(
                path,
                UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }
        catch (PlatformNotSupportedException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private static void SetPrivateFilePermissions(string path)
    {
        try
        {
            File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        }
        catch (PlatformNotSupportedException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}