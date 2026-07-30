using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;

namespace Matgate.Web;

public sealed class NetworkToolsAdminGuardMiddleware
{
    private readonly RequestDelegate _next;
    private readonly bool _enabled;

    public NetworkToolsAdminGuardMiddleware(RequestDelegate next, IConfiguration configuration)
    {
        _next = next;
        _enabled = bool.TryParse(
                Environment.GetEnvironmentVariable("JULGATE_ENABLE_NETWORK_TOOLS")
                ?? configuration["Julgate:EnableNetworkTools"],
                out var configured)
            && configured;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        if (!_enabled || !RequiresAdministrator(context.Request.Path))
        {
            await _next(context);
            return;
        }

        var authentication = await context.AuthenticateAsync(
            CookieAuthenticationDefaults.AuthenticationScheme);
        if (!authentication.Succeeded || authentication.Principal is null)
        {
            await _next(context);
            return;
        }

        context.User = authentication.Principal;
        if (!IsAdministrator(context.User))
        {
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            context.Response.ContentType = "text/plain; charset=utf-8";
            await context.Response.WriteAsync(
                "Administrator access is required.",
                context.RequestAborted);
            return;
        }

        await _next(context);
    }

    internal static bool RequiresAdministrator(PathString path)
    {
        return path.StartsWithSegments("/tools")
            || path.StartsWithSegments("/api/tools");
    }

    internal static bool IsAdministrator(ClaimsPrincipal principal)
    {
        return principal.Claims.Any(claim =>
            claim.Type == ClaimTypes.Role
            && string.Equals(claim.Value, "admin", StringComparison.OrdinalIgnoreCase));
    }
}
