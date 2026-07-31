namespace Matgate.Web;

/// <summary>
/// Keeps the product-facing Julgate browser contract compatible with the legacy
/// endpoint implementation and permits only explicitly supported same-origin shell pages.
/// </summary>
public sealed class JulgateShellCompatibilityMiddleware(RequestDelegate next)
{
    internal const string JulgateCsrfHeader = "X-Julgate-Csrf";
    internal const string LegacyCsrfHeader = "X-Matgate-Csrf";

    public async Task InvokeAsync(HttpContext context)
    {
        NormalizeCsrfHeader(context.Request);

        context.Response.OnStarting(() =>
        {
            if (IsSafeEmbeddedPage(context.Request)
                && context.Response.StatusCode is >= 200 and < 400
                && context.Response.ContentType?.StartsWith("text/html", StringComparison.OrdinalIgnoreCase) == true)
            {
                context.Response.Headers["X-Frame-Options"] = "SAMEORIGIN";

                var policy = context.Response.Headers.ContentSecurityPolicy.ToString();
                if (!string.IsNullOrWhiteSpace(policy))
                {
                    context.Response.Headers.ContentSecurityPolicy = policy.Replace(
                        "frame-ancestors 'none'",
                        "frame-ancestors 'self'",
                        StringComparison.OrdinalIgnoreCase);
                }
            }

            return Task.CompletedTask;
        });

        await next(context);
    }

    internal static void NormalizeCsrfHeader(HttpRequest request)
    {
        if (request.Headers.ContainsKey(LegacyCsrfHeader)
            || !request.Headers.TryGetValue(JulgateCsrfHeader, out var julgateToken)
            || string.IsNullOrWhiteSpace(julgateToken.ToString()))
        {
            return;
        }

        // EndpointMapping still validates the historical header internally. Keep that
        // implementation detail behind this compatibility boundary while browsers use
        // the correctly branded Julgate header.
        request.Headers[LegacyCsrfHeader] = julgateToken;
    }

    internal static bool IsSafeEmbeddedPage(HttpRequest request)
    {
        if (!HttpMethods.IsGet(request.Method)
            || !string.Equals(request.Query["embed"].ToString(), "1", StringComparison.Ordinal))
        {
            return false;
        }

        var path = request.Path.Value ?? string.Empty;
        return path.Equals("/account", StringComparison.OrdinalIgnoreCase)
            || path.Equals("/about", StringComparison.OrdinalIgnoreCase)
            || path.Equals("/tools", StringComparison.OrdinalIgnoreCase)
            || path.StartsWith("/admin/", StringComparison.OrdinalIgnoreCase)
            || path.Equals("/workspaces", StringComparison.OrdinalIgnoreCase)
            || path.StartsWith("/workspaces/", StringComparison.OrdinalIgnoreCase);
    }
}
