using FluentFTP;
using Matgate.Models;
using Renci.SshNet;
using SMBLibrary;
using SMBLibrary.Client;
using SmbFileAttributes = SMBLibrary.FileAttributes;

namespace Matgate.Services;

public sealed class FileGatewaySecurityDecorator : IFileGatewayService
{
    private readonly FileGatewayService _inner;
    private readonly TimeSpan _operationTimeout;
    private readonly long _maxUploadBytes;
    private readonly long _maxDownloadBytes;
    private readonly int _maxDirectoryEntries;

    public FileGatewaySecurityDecorator(FileGatewayService inner, IConfiguration configuration)
    {
        _inner = inner;
        _operationTimeout = TimeSpan.FromSeconds(ReadPositiveInt(configuration, "JULGATE_FILE_OPERATION_TIMEOUT_SECONDS", 120));
        _maxUploadBytes = ReadPositiveLong(configuration, "JULGATE_MAX_UPLOAD_BYTES", 512L * 1024L * 1024L);
        _maxDownloadBytes = ReadPositiveLong(configuration, "JULGATE_MAX_DOWNLOAD_BYTES", 2L * 1024L * 1024L * 1024L);
        _maxDirectoryEntries = ReadPositiveInt(configuration, "JULGATE_MAX_DIRECTORY_ENTRIES", 10_000);
    }

    public async Task<FileGatewayListResult> ListAsync(
        ServerEndpoint server,
        string? path,
        CancellationToken cancellationToken = default)
    {
        var normalized = NormalizeVirtualPath(path);
        await EnsureRemotePathSafeAsync(server, normalized, allowMissingLeaf: false, cancellationToken);
        var result = await RunAsync(token => _inner.ListAsync(server, normalized, token), cancellationToken);
        if (result.Entries.Count > _maxDirectoryEntries)
        {
            throw new FileTransferLimitExceededException(
                $"Directory contains more than the configured {_maxDirectoryEntries} entries.");
        }

        return result;
    }

    public async Task<FileGatewayDownload> DownloadAsync(
        ServerEndpoint server,
        string? path,
        CancellationToken cancellationToken = default)
    {
        var normalized = NormalizeVirtualPath(path);
        EnsureNotRoot(normalized);
        await EnsureRemotePathSafeAsync(server, normalized, allowMissingLeaf: false, cancellationToken);
        var info = await RunAsync(token => _inner.GetFileInfoAsync(server, normalized, token), cancellationToken);
        EnsureDownloadSize(info.Length);
        return await RunAsync(token => _inner.DownloadAsync(server, normalized, token), cancellationToken);
    }

    public async Task<FileGatewayFileInfo> GetFileInfoAsync(
        ServerEndpoint server,
        string? path,
        CancellationToken cancellationToken = default)
    {
        var normalized = NormalizeVirtualPath(path);
        EnsureNotRoot(normalized);
        await EnsureRemotePathSafeAsync(server, normalized, allowMissingLeaf: false, cancellationToken);
        var info = await RunAsync(token => _inner.GetFileInfoAsync(server, normalized, token), cancellationToken);
        EnsureDownloadSize(info.Length);
        return info;
    }

    public async Task CopyRangeAsync(
        ServerEndpoint server,
        string? path,
        Stream output,
        long offset,
        long length,
        CancellationToken cancellationToken = default)
    {
        if (offset < 0 || length < 0 || length > _maxDownloadBytes)
        {
            throw new FileTransferLimitExceededException("Requested file range exceeds the configured download limit.");
        }

        var normalized = NormalizeVirtualPath(path);
        EnsureNotRoot(normalized);
        await EnsureRemotePathSafeAsync(server, normalized, allowMissingLeaf: false, cancellationToken);
        await RunAsync(token => _inner.CopyRangeAsync(server, normalized, output, offset, length, token), cancellationToken);
    }

    public async Task UploadAsync(
        ServerEndpoint server,
        string? path,
        Stream content,
        string fileName,
        CancellationToken cancellationToken = default)
    {
        var normalizedDirectory = NormalizeVirtualPath(path);
        var normalizedName = NormalizeLeafName(fileName);
        EnsureLocalUploadSourceSafe(content);
        await EnsureRemotePathSafeAsync(server, normalizedDirectory, allowMissingLeaf: false, cancellationToken);

        if (content.CanSeek && content.Length - content.Position > _maxUploadBytes)
        {
            throw new FileTransferLimitExceededException("Upload exceeds the configured size limit.");
        }

        FileTransferBudget.ConsumeEntry();
        await using var bounded = new BoundedReadStream(content, _maxUploadBytes, countAgainstArchiveBudget: true);
        await RunAsync(
            token => _inner.UploadAsync(server, normalizedDirectory, bounded, normalizedName, token),
            cancellationToken);
    }

    public async Task CreateFileAsync(
        ServerEndpoint server,
        string? path,
        string fileName,
        CancellationToken cancellationToken = default)
    {
        var normalizedDirectory = NormalizeVirtualPath(path);
        var normalizedName = NormalizeLeafName(fileName);
        await EnsureRemotePathSafeAsync(server, normalizedDirectory, allowMissingLeaf: false, cancellationToken);
        FileTransferBudget.ConsumeEntry();
        await RunAsync(
            token => _inner.CreateFileAsync(server, normalizedDirectory, normalizedName, token),
            cancellationToken);
    }

    public async Task CreateDirectoryAsync(
        ServerEndpoint server,
        string? path,
        string directoryName,
        CancellationToken cancellationToken = default)
    {
        var normalizedDirectory = NormalizeVirtualPath(path);
        var normalizedName = NormalizeLeafName(directoryName);
        await EnsureRemotePathSafeAsync(server, normalizedDirectory, allowMissingLeaf: false, cancellationToken);
        FileTransferBudget.ConsumeEntry();
        await RunAsync(
            token => _inner.CreateDirectoryAsync(server, normalizedDirectory, normalizedName, token),
            cancellationToken);
    }

    public async Task DeleteAsync(
        ServerEndpoint server,
        string? path,
        CancellationToken cancellationToken = default)
    {
        var normalized = NormalizeVirtualPath(path);
        EnsureNotRoot(normalized);
        await EnsureRemotePathSafeAsync(server, normalized, allowMissingLeaf: false, cancellationToken);
        await RunAsync(token => _inner.DeleteAsync(server, normalized, token), cancellationToken);
    }

    internal static string NormalizeVirtualPath(string? path)
    {
        var value = DecodeRepeatedly(path ?? "/").Replace('\\', '/');
        if (value.IndexOf('\0') >= 0 || value.Any(char.IsControl))
        {
            throw new InvalidOperationException("Path contains control characters.");
        }

        if (value.Length > 4096)
        {
            throw new InvalidOperationException("Path is too long.");
        }

        var parts = value.Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        foreach (var part in parts)
        {
            if (part is "." or "..")
            {
                throw new InvalidOperationException("Path traversal is not allowed.");
            }

            if (part.Length > 255)
            {
                throw new InvalidOperationException("A path segment is too long.");
            }
        }

        return parts.Length == 0 ? "/" : "/" + string.Join('/', parts);
    }

    internal static string NormalizeLeafName(string? name)
    {
        var decoded = DecodeRepeatedly(name ?? "").Trim();
        if (string.IsNullOrWhiteSpace(decoded)
            || decoded is "." or ".."
            || decoded.Length > 255
            || decoded.Contains('/')
            || decoded.Contains('\\')
            || decoded.IndexOf('\0') >= 0
            || decoded.Any(char.IsControl))
        {
            throw new InvalidOperationException("Invalid file or directory name.");
        }

        return decoded;
    }

    private static string DecodeRepeatedly(string value)
    {
        for (var attempt = 0; attempt < 3; attempt++)
        {
            string decoded;
            try
            {
                decoded = Uri.UnescapeDataString(value);
            }
            catch (UriFormatException exception)
            {
                throw new InvalidOperationException("Path encoding is invalid.", exception);
            }

            if (string.Equals(decoded, value, StringComparison.Ordinal))
            {
                return decoded;
            }

            value = decoded;
        }

        return value;
    }

    private async Task<T> RunAsync<T>(Func<CancellationToken, Task<T>> operation, CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(_operationTimeout);
        try
        {
            return await operation(timeout.Token).WaitAsync(_operationTimeout, cancellationToken);
        }
        catch (TimeoutException exception)
        {
            timeout.Cancel();
            throw new InvalidOperationException(
                $"File gateway operation exceeded the configured {_operationTimeout.TotalSeconds:0} second timeout.",
                exception);
        }
        catch (OperationCanceledException exception) when (!cancellationToken.IsCancellationRequested && timeout.IsCancellationRequested)
        {
            throw new InvalidOperationException(
                $"File gateway operation exceeded the configured {_operationTimeout.TotalSeconds:0} second timeout.",
                exception);
        }
    }

    private async Task RunAsync(Func<CancellationToken, Task> operation, CancellationToken cancellationToken)
    {
        await RunAsync(async token =>
        {
            await operation(token);
            return true;
        }, cancellationToken);
    }

    private void EnsureDownloadSize(long length)
    {
        if (length < 0 || length > _maxDownloadBytes)
        {
            throw new FileTransferLimitExceededException("Download exceeds the configured size limit.");
        }
    }

    private static void EnsureNotRoot(string path)
    {
        if (path == "/")
        {
            throw new InvalidOperationException("The root path cannot be used for this operation.");
        }
    }

    private static void EnsureLocalUploadSourceSafe(Stream content)
    {
        if (content is not FileStream fileStream || string.IsNullOrWhiteSpace(fileStream.Name))
        {
            return;
        }

        var file = new FileInfo(Path.GetFullPath(fileStream.Name));
        if (!string.IsNullOrEmpty(file.LinkTarget))
        {
            throw new InvalidOperationException("Uploading symbolic links is not allowed.");
        }

        for (var directory = file.Directory; directory is not null; directory = directory.Parent)
        {
            if (!string.IsNullOrEmpty(directory.LinkTarget))
            {
                throw new InvalidOperationException("Uploading through a symbolic-link directory is not allowed.");
            }
        }
    }

    private async Task EnsureRemotePathSafeAsync(
        ServerEndpoint server,
        string virtualPath,
        bool allowMissingLeaf,
        CancellationToken cancellationToken)
    {
        switch (server.Protocol)
        {
            case ServerProtocol.Sftp:
                await RunAsync(
                    token => Task.Run(() => EnsureSftpPathSafe(server, virtualPath, allowMissingLeaf, token), token),
                    cancellationToken);
                break;
            case ServerProtocol.Ftp:
                await RunAsync(
                    token => EnsureFtpPathSafeAsync(server, virtualPath, allowMissingLeaf, token),
                    cancellationToken);
                break;
            case ServerProtocol.Smb:
                await RunAsync(
                    token => Task.Run(() => EnsureSmbPathSafe(server, virtualPath, allowMissingLeaf, token), token),
                    cancellationToken);
                break;
        }
    }

    private static void EnsureSftpPathSafe(
        ServerEndpoint server,
        string virtualPath,
        bool allowMissingLeaf,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(server.UserName))
        {
            throw new InvalidOperationException("SFTP requires a target user.");
        }

        var parts = RemoteRootParts(server.FileRootPath)
            .Concat(VirtualParts(virtualPath))
            .ToArray();
        using var client = new SftpClient(server.Host, server.Port, server.UserName, server.Password)
        {
            OperationTimeout = TimeSpan.FromSeconds(60)
        };
        client.Connect();

        var current = "/";
        for (var index = 0; index < parts.Length; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var entry = client.ListDirectory(current)
                .FirstOrDefault(candidate => string.Equals(candidate.Name, parts[index], StringComparison.Ordinal));
            if (entry is null)
            {
                if (allowMissingLeaf && index == parts.Length - 1)
                {
                    return;
                }

                throw new InvalidOperationException("Remote path does not exist.");
            }

            if (entry.IsSymbolicLink)
            {
                throw new InvalidOperationException("Symbolic links are not allowed in file gateway paths.");
            }

            current = entry.FullName;
        }
    }

    private static async Task EnsureFtpPathSafeAsync(
        ServerEndpoint server,
        string virtualPath,
        bool allowMissingLeaf,
        CancellationToken cancellationToken)
    {
        var parts = RemoteRootParts(server.FileRootPath)
            .Concat(VirtualParts(virtualPath))
            .ToArray();
        using var client = new AsyncFtpClient(server.Host, server.UserName, server.Password, server.Port);
        await client.Connect(cancellationToken);

        var current = "/";
        for (var index = 0; index < parts.Length; index++)
        {
            var listing = await client.GetListing(current, cancellationToken);
            var entry = listing.FirstOrDefault(candidate =>
                string.Equals(candidate.Name, parts[index], StringComparison.Ordinal));
            if (entry is null)
            {
                if (allowMissingLeaf && index == parts.Length - 1)
                {
                    return;
                }

                throw new InvalidOperationException("Remote path does not exist.");
            }

            if (entry.Type == FtpObjectType.Link)
            {
                throw new InvalidOperationException("Symbolic links are not allowed in file gateway paths.");
            }

            current = entry.FullName;
        }

        await client.Disconnect(cancellationToken);
    }

    private static void EnsureSmbPathSafe(
        ServerEndpoint server,
        string virtualPath,
        bool allowMissingLeaf,
        CancellationToken cancellationToken)
    {
        var rootParts = ParseSmbRoot(server);
        var virtualParts = VirtualParts(virtualPath).ToList();
        var share = rootParts.Share;
        var pathParts = rootParts.PathParts.ToList();
        if (string.IsNullOrWhiteSpace(share))
        {
            share = virtualParts.FirstOrDefault() ?? "";
            if (virtualParts.Count > 0)
            {
                virtualParts.RemoveAt(0);
            }
        }

        if (string.IsNullOrWhiteSpace(share))
        {
            return;
        }

        pathParts.AddRange(virtualParts);
        var client = new SMB2Client();
        if (!client.Connect(server.Host, SMBTransportType.DirectTCPTransport))
        {
            throw new InvalidOperationException("SMB connection could not be established.");
        }

        try
        {
            var loginStatus = client.Login(server.Domain, server.UserName, server.Password);
            CheckSmbStatus(loginStatus, "SMB login");
            var fileStore = client.TreeConnect(share, out var treeStatus);
            CheckSmbStatus(treeStatus, "SMB share connection");
            if (fileStore is not SMB2FileStore store)
            {
                fileStore.Disconnect();
                throw new InvalidOperationException("SMB2 is required for safe file access.");
            }

            try
            {
                var parentParts = new List<string>();
                for (var index = 0; index < pathParts.Count; index++)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    object? directoryHandle = null;
                    try
                    {
                        var parentPath = string.Join('\\', parentParts);
                        var status = store.CreateFile(
                            out directoryHandle,
                            out _,
                            parentPath,
                            AccessMask.GENERIC_READ,
                            SmbFileAttributes.Directory,
                            ShareAccess.Read | ShareAccess.Write | ShareAccess.Delete,
                            CreateDisposition.FILE_OPEN,
                            CreateOptions.FILE_DIRECTORY_FILE,
                            null!);
                        CheckSmbStatus(status, "SMB parent directory open");

                        status = store.QueryDirectory(
                            out var entries,
                            directoryHandle,
                            pathParts[index],
                            FileInformationClass.FileDirectoryInformation);
                        var entry = entries?
                            .OfType<FileDirectoryInformation>()
                            .FirstOrDefault(candidate =>
                                string.Equals(candidate.FileName, pathParts[index], StringComparison.OrdinalIgnoreCase));
                        if (entry is null)
                        {
                            if (allowMissingLeaf && index == pathParts.Count - 1)
                            {
                                return;
                            }

                            if (status != NTStatus.STATUS_SUCCESS && status != NTStatus.STATUS_NO_MORE_FILES)
                            {
                                CheckSmbStatus(status, "SMB path query");
                            }

                            throw new InvalidOperationException("Remote path does not exist.");
                        }

                        if (entry.FileAttributes.HasFlag(SmbFileAttributes.ReparsePoint))
                        {
                            throw new InvalidOperationException("SMB reparse points are not allowed in file gateway paths.");
                        }
                    }
                    finally
                    {
                        if (directoryHandle is not null)
                        {
                            store.CloseFile(directoryHandle);
                        }
                    }

                    parentParts.Add(pathParts[index]);
                }
            }
            finally
            {
                store.Disconnect();
            }
        }
        finally
        {
            try
            {
                client.Logoff();
            }
            finally
            {
                client.Disconnect();
            }
        }
    }

    private static IEnumerable<string> RemoteRootParts(string? root)
    {
        return (root ?? "")
            .Replace('\\', '/')
            .Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(NormalizeLeafName);
    }

    private static IEnumerable<string> VirtualParts(string path)
    {
        return NormalizeVirtualPath(path)
            .Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }

    private static (string Share, IReadOnlyList<string> PathParts) ParseSmbRoot(ServerEndpoint server)
    {
        var normalized = (server.FileRootPath ?? "").Trim().Replace('\\', '/');
        var parts = normalized
            .Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(NormalizeLeafName)
            .ToList();
        if (parts.Count > 1 && string.Equals(parts[0], server.Host, StringComparison.OrdinalIgnoreCase))
        {
            parts.RemoveAt(0);
        }

        return parts.Count == 0
            ? ("", Array.Empty<string>())
            : (parts[0], parts.Skip(1).ToArray());
    }

    private static void CheckSmbStatus(NTStatus status, string operation)
    {
        if (status != NTStatus.STATUS_SUCCESS)
        {
            throw new InvalidOperationException($"{operation} failed: {status}");
        }
    }

    private static int ReadPositiveInt(IConfiguration configuration, string name, int fallback)
    {
        var raw = Environment.GetEnvironmentVariable(name)
            ?? configuration[$"Julgate:{name["JULGATE_".Length..]}"];
        return int.TryParse(raw, out var value) && value > 0 ? value : fallback;
    }

    private static long ReadPositiveLong(IConfiguration configuration, string name, long fallback)
    {
        var raw = Environment.GetEnvironmentVariable(name)
            ?? configuration[$"Julgate:{name["JULGATE_".Length..]}"];
        return long.TryParse(raw, out var value) && value > 0 ? value : fallback;
    }
}
