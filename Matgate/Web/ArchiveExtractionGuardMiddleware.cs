using Matgate.Services;

namespace Matgate.Web;

public sealed class ArchiveExtractionGuardMiddleware
{
    private readonly RequestDelegate _next;
    private readonly bool _enabled;
    private readonly long _maxExpandedBytes;
    private readonly int _maxEntries;
    private readonly SemaphoreSlim _gate;

    public ArchiveExtractionGuardMiddleware(RequestDelegate next, IConfiguration configuration)
    {
        _next = next;
        _enabled = ReadBoolean(configuration, "JULGATE_ENABLE_ARCHIVE_EXTRACTION", false);
        _maxExpandedBytes = ReadPositiveLong(
            configuration,
            "JULGATE_MAX_ARCHIVE_EXPANDED_BYTES",
            256L * 1024L * 1024L);
        _maxEntries = ReadPositiveInt(configuration, "JULGATE_MAX_ARCHIVE_ENTRIES", 4096);
        _gate = new SemaphoreSlim(
            ReadPositiveInt(configuration, "JULGATE_MAX_CONCURRENT_ARCHIVE_EXTRACTIONS", 1));
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var isExtractionRequest = await IsExtractionRequestAsync(context.Request);
        if (!isExtractionRequest)
        {
            await _next(context);
            return;
        }

        if (!_enabled)
        {
            context.Response.StatusCode = StatusCodes.Status404NotFound;
            context.Response.ContentType = "text/plain; charset=utf-8";
            await context.Response.WriteAsync(
                "Archive extraction is disabled.",
                context.RequestAborted);
            return;
        }

        await _gate.WaitAsync(context.RequestAborted);
        try
        {
            using var budget = FileTransferBudget.Begin(_maxExpandedBytes, _maxEntries);
            context.Items["Julgate.ArchiveExtraction"] = true;
            await _next(context);
        }
        finally
        {
            _gate.Release();
        }
    }

    internal static async Task<bool> IsExtractionRequestAsync(HttpRequest request)
    {
        var path = request.Path.Value ?? "";
        if (path.StartsWith("/api/files/", StringComparison.OrdinalIgnoreCase)
            && path.EndsWith("/extract", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (!request.HasFormContentType)
        {
            return false;
        }

        try
        {
            var form = await request.ReadFormAsync(request.HttpContext.RequestAborted);
            return form.TryGetValue("unzip", out var values)
                && values.Any(IsTruthy);
        }
        catch (Exception exception) when (exception is InvalidDataException or IOException or BadHttpRequestException)
        {
            return false;
        }
    }

    internal static bool IsTruthy(string? value)
    {
        return string.Equals(value, "true", StringComparison.OrdinalIgnoreCase)
            || string.Equals(value, "1", StringComparison.Ordinal)
            || string.Equals(value, "yes", StringComparison.OrdinalIgnoreCase)
            || string.Equals(value, "on", StringComparison.OrdinalIgnoreCase);
    }

    private static bool ReadBoolean(IConfiguration configuration, string name, bool fallback)
    {
        var raw = Environment.GetEnvironmentVariable(name)
            ?? configuration[$"Julgate:{name["JULGATE_".Length..]}"];
        return bool.TryParse(raw, out var value) ? value : fallback;
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
