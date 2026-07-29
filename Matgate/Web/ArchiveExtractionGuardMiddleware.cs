namespace Matgate.Web;

public sealed class ArchiveExtractionGuardMiddleware
{
    private readonly RequestDelegate _next;
    private readonly bool _enabled;

    public ArchiveExtractionGuardMiddleware(RequestDelegate next, IConfiguration configuration)
    {
        _next = next;
        _enabled = bool.TryParse(
                Environment.GetEnvironmentVariable("JULGATE_ENABLE_ARCHIVE_EXTRACTION")
                ?? configuration["Julgate:EnableArchiveExtraction"],
                out var configured)
            && configured;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        if (!_enabled && await IsExtractionRequestAsync(context.Request))
        {
            context.Response.StatusCode = StatusCodes.Status404NotFound;
            context.Response.ContentType = "text/plain; charset=utf-8";
            await context.Response.WriteAsync(
                "Archive extraction is disabled.",
                context.RequestAborted);
            return;
        }

        await _next(context);
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
                && values.Any(value => bool.TryParse(value, out var unzip) && unzip);
        }
        catch (InvalidDataException)
        {
            return false;
        }
    }
}
