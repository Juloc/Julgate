using System.Security.Claims;
using System.Text.Json;

namespace Matgate.Services;

public sealed class SecurityAuditService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly string _auditPath;
    private readonly ILogger<SecurityAuditService> _logger;

    public SecurityAuditService(IConfiguration configuration, IHostEnvironment environment, ILogger<SecurityAuditService> logger)
    {
        _logger = logger;
        var configured = Environment.GetEnvironmentVariable("JULGATE_DATA_DIR")
            ?? Environment.GetEnvironmentVariable("MATGATE_DATA_DIR")
            ?? configuration["Matgate:DataDirectory"];
        var dataDirectory = Path.GetFullPath(string.IsNullOrWhiteSpace(configured)
            ? Path.Combine(environment.ContentRootPath, "data")
            : configured);
        var auditDirectory = Path.Combine(dataDirectory, "audit");
        Directory.CreateDirectory(auditDirectory);
        SetPrivateDirectoryPermissions(auditDirectory);
        _auditPath = Path.Combine(auditDirectory, "security.jsonl");
    }

    public async Task WriteRequestAsync(HttpContext context, string action, CancellationToken cancellationToken = default)
    {
        var userName = context.User.FindFirstValue(ClaimTypes.Name)
            ?? context.User.Identity?.Name
            ?? "anonymous";
        var record = new SecurityAuditRecord(
            DateTimeOffset.UtcNow,
            action,
            userName,
            context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            context.Request.Method,
            context.Request.Path.Value ?? "/",
            context.Response.StatusCode,
            context.TraceIdentifier);

        await _gate.WaitAsync(cancellationToken);
        try
        {
            var json = JsonSerializer.Serialize(record, JsonOptions);
            await File.AppendAllTextAsync(_auditPath, json + Environment.NewLine, cancellationToken);
            SetPrivateFilePermissions(_auditPath);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            _logger.LogError(exception, "Julgate security audit record could not be persisted.");
        }
        finally
        {
            _gate.Release();
        }
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

    private sealed record SecurityAuditRecord(
        DateTimeOffset TimestampUtc,
        string Action,
        string User,
        string RemoteAddress,
        string Method,
        string Path,
        int StatusCode,
        string TraceId);
}
