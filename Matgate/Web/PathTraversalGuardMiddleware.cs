namespace Matgate.Web;

public sealed class PathTraversalGuardMiddleware(RequestDelegate next)
{
    private const int MaxCheckedValueLength = 8192;

    public async Task InvokeAsync(HttpContext context)
    {
        if (ContainsUnsafePath(context.Request.Path.Value)
            || ContainsUnsafePath(context.Request.QueryString.Value)
            || QueryContainsUnsafeValue(context.Request.Query))
        {
            await RejectAsync(context);
            return;
        }

        if (context.Request.HasFormContentType)
        {
            IFormCollection form;
            try
            {
                form = await context.Request.ReadFormAsync(context.RequestAborted);
            }
            catch (InvalidDataException)
            {
                await RejectAsync(context);
                return;
            }

            if (form.Any(field => field.Value.Any(ContainsUnsafePath))
                || form.Files.Any(file => IsUnsafeLeafName(file.FileName)))
            {
                await RejectAsync(context);
                return;
            }
        }

        await next(context);
    }

    internal static bool ContainsUnsafePath(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return false;
        }

        if (value.Length > MaxCheckedValueLength || value.Any(character => character == '\0'))
        {
            return true;
        }

        var decoded = value;
        for (var pass = 0; pass < 3; pass++)
        {
            try
            {
                var next = Uri.UnescapeDataString(decoded);
                if (string.Equals(next, decoded, StringComparison.Ordinal))
                {
                    break;
                }

                decoded = next;
            }
            catch (UriFormatException)
            {
                return true;
            }
        }

        var normalized = decoded.Replace('\\', '/');
        return normalized
            .Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Any(segment => segment is "." or "..");
    }

    internal static bool IsUnsafeLeafName(string? value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > 255)
        {
            return true;
        }

        return ContainsUnsafePath(value)
            || value.Contains('/')
            || value.Contains('\\')
            || value.Any(char.IsControl);
    }

    private static bool QueryContainsUnsafeValue(IQueryCollection query)
    {
        return query.Any(parameter => parameter.Value.Any(ContainsUnsafePath));
    }

    private static async Task RejectAsync(HttpContext context)
    {
        context.Response.StatusCode = StatusCodes.Status400BadRequest;
        context.Response.ContentType = "text/plain; charset=utf-8";
        await context.Response.WriteAsync("Invalid path input.", context.RequestAborted);
    }
}
