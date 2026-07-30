namespace Matgate.Web;

public sealed class CrossOriginGuardMiddleware
{
    private readonly RequestDelegate _next;
    private readonly bool _forceHttps;

    public CrossOriginGuardMiddleware(RequestDelegate next, IConfiguration configuration)
    {
        _next = next;
        _forceHttps = bool.TryParse(
                Environment.GetEnvironmentVariable("JULGATE_REQUIRE_SECURE_COOKIES")
                ?? configuration["Julgate:RequireSecureCookies"],
                out var configured)
            && configured;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        if (IsUnsafeMethod(context.Request.Method)
            && !IsAllowed(context.Request, _forceHttps))
        {
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            context.Response.ContentType = "text/plain; charset=utf-8";
            await context.Response.WriteAsync(
                "Cross-origin state-changing requests are not allowed.",
                context.RequestAborted);
            return;
        }

        await _next(context);
    }

    internal static bool IsAllowed(HttpRequest request, bool forceHttps)
    {
        var fetchSite = request.Headers["Sec-Fetch-Site"].ToString().Trim();
        if (string.Equals(fetchSite, "cross-site", StringComparison.OrdinalIgnoreCase)
            || string.Equals(fetchSite, "same-site", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var expectedScheme = forceHttps ? Uri.UriSchemeHttps : request.Scheme;
        var expectedAuthority = request.Host.Value;

        var origin = request.Headers.Origin.ToString();
        if (!string.IsNullOrWhiteSpace(origin))
        {
            return IsExpectedOrigin(origin, expectedScheme, expectedAuthority);
        }

        var referer = request.Headers.Referer.ToString();
        if (!string.IsNullOrWhiteSpace(referer))
        {
            return IsExpectedOrigin(referer, expectedScheme, expectedAuthority);
        }

        return string.IsNullOrWhiteSpace(fetchSite)
            || string.Equals(fetchSite, "same-origin", StringComparison.OrdinalIgnoreCase)
            || string.Equals(fetchSite, "none", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsExpectedOrigin(string value, string expectedScheme, string expectedAuthority)
    {
        return Uri.TryCreate(value, UriKind.Absolute, out var uri)
            && string.Equals(uri.Scheme, expectedScheme, StringComparison.OrdinalIgnoreCase)
            && string.Equals(uri.Authority, expectedAuthority, StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsUnsafeMethod(string method)
    {
        return HttpMethods.IsPost(method)
            || HttpMethods.IsPut(method)
            || HttpMethods.IsPatch(method)
            || HttpMethods.IsDelete(method);
    }
}
