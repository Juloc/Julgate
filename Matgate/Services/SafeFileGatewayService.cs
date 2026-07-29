using Matgate.Models;

namespace Matgate.Services;

public sealed class SafeFileGatewayService : IFileGatewayService
{
    private const int MaxPathLength = 4096;
    private const int MaxSegmentLength = 255;
    private readonly FileGatewayService _inner;

    public SafeFileGatewayService(FileGatewayService inner)
    {
        _inner = inner;
    }

    public Task<FileGatewayListResult> ListAsync(
        ServerEndpoint server,
        string? path,
        CancellationToken cancellationToken = default)
    {
        ValidateServer(server);
        ValidateVirtualPath(path);
        return _inner.ListAsync(server, path, cancellationToken);
    }

    public Task<FileGatewayDownload> DownloadAsync(
        ServerEndpoint server,
        string? path,
        CancellationToken cancellationToken = default)
    {
        ValidateServer(server);
        ValidateVirtualPath(path);
        return _inner.DownloadAsync(server, path, cancellationToken);
    }

    public Task<FileGatewayFileInfo> GetFileInfoAsync(
        ServerEndpoint server,
        string? path,
        CancellationToken cancellationToken = default)
    {
        ValidateServer(server);
        ValidateVirtualPath(path);
        return _inner.GetFileInfoAsync(server, path, cancellationToken);
    }

    public Task CopyRangeAsync(
        ServerEndpoint server,
        string? path,
        Stream output,
        long offset,
        long length,
        CancellationToken cancellationToken = default)
    {
        ValidateServer(server);
        ValidateVirtualPath(path);
        if (offset < 0 || length < 0)
        {
            throw new InvalidOperationException("Ungueltiger Dateibereich.");
        }

        return _inner.CopyRangeAsync(server, path, output, offset, length, cancellationToken);
    }

    public Task UploadAsync(
        ServerEndpoint server,
        string? path,
        Stream content,
        string fileName,
        CancellationToken cancellationToken = default)
    {
        ValidateServer(server);
        ValidateVirtualPath(path);
        ValidateLeafName(fileName);
        return _inner.UploadAsync(server, path, content, fileName, cancellationToken);
    }

    public Task CreateFileAsync(
        ServerEndpoint server,
        string? path,
        string fileName,
        CancellationToken cancellationToken = default)
    {
        ValidateServer(server);
        ValidateVirtualPath(path);
        ValidateLeafName(fileName);
        return _inner.CreateFileAsync(server, path, fileName, cancellationToken);
    }

    public Task CreateDirectoryAsync(
        ServerEndpoint server,
        string? path,
        string directoryName,
        CancellationToken cancellationToken = default)
    {
        ValidateServer(server);
        ValidateVirtualPath(path);
        ValidateLeafName(directoryName);
        return _inner.CreateDirectoryAsync(server, path, directoryName, cancellationToken);
    }

    public Task DeleteAsync(
        ServerEndpoint server,
        string? path,
        CancellationToken cancellationToken = default)
    {
        ValidateServer(server);
        ValidateVirtualPath(path);
        return _inner.DeleteAsync(server, path, cancellationToken);
    }

    internal static void ValidateVirtualPath(string? path)
    {
        ValidatePath(path, allowRoot: true, "Dateipfad");
    }

    internal static void ValidateConfiguredRoot(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        ValidatePath(path, allowRoot: true, "Server-Root-Pfad");
    }

    internal static void ValidateLeafName(string? name)
    {
        var value = (name ?? "").Trim();
        if (string.IsNullOrWhiteSpace(value)
            || value is "." or ".."
            || value.Length > MaxSegmentLength
            || value.Contains('/')
            || value.Contains('\\')
            || ContainsUnsafeCharacters(value))
        {
            throw new InvalidOperationException("Ungueltiger Datei- oder Ordnername.");
        }
    }

    private static void ValidateServer(ServerEndpoint server)
    {
        ArgumentNullException.ThrowIfNull(server);
        ValidateConfiguredRoot(server.FileRootPath);
    }

    private static void ValidatePath(string? path, bool allowRoot, string fieldName)
    {
        var value = string.IsNullOrWhiteSpace(path) ? "/" : path.Trim();
        if (value.Length > MaxPathLength || ContainsUnsafeCharacters(value))
        {
            throw new InvalidOperationException($"{fieldName} ist ungueltig.");
        }

        var normalized = value.Replace('\\', '/');
        var segments = normalized.Split('/', StringSplitOptions.RemoveEmptyEntries);
        foreach (var segment in segments)
        {
            if (segment is "." or ".." || segment.Length > MaxSegmentLength)
            {
                throw new InvalidOperationException($"{fieldName} enthaelt unzulaessige Pfadsegmente.");
            }
        }

        if (!allowRoot && segments.Length == 0)
        {
            throw new InvalidOperationException($"{fieldName} darf nicht auf den Root-Pfad zeigen.");
        }
    }

    private static bool ContainsUnsafeCharacters(string value)
    {
        return value.Any(character => character == '\0' || char.IsControl(character));
    }
}
