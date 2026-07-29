using Microsoft.Extensions.Primitives;

namespace Matgate.Web;

public sealed class WorkspaceCookieHardeningMiddleware(RequestDelegate next)
{
    private static readonly string[] WorkspaceCookiePrefixes =
    [
        "Matgate.Workspace.",
        "Julgate.Workspace."
    ];

    public async Task InvokeAsync(HttpContext context)
    {
        context.Response.OnStarting(() =>
        {
            if (!context.Response.Headers.TryGetValue("Set-Cookie", out var values))
            {
                return Task.CompletedTask;
            }

            var rewritten = values
                .Select(value => Rewrite(value ?? "", context.Request.IsHttps))
                .ToArray();
            context.Response.Headers["Set-Cookie"] = new StringValues(rewritten);
            return Task.CompletedTask;
        });

        await next(context);
    }

    internal static string Rewrite(string header, bool isHttps)
    {
        if (!IsWorkspaceCookie(header))
        {
            return header;
        }

        var result = header.Replace("SameSite=Lax", "SameSite=Strict", StringComparison.OrdinalIgnoreCase);
        if (!result.Contains("SameSite=", StringComparison.OrdinalIgnoreCase))
        {
            result += "; SameSite=Strict";
        }

        if (!result.Contains("; HttpOnly", StringComparison.OrdinalIgnoreCase))
        {
            result += "; HttpOnly";
        }

        if (isHttps && !result.Contains("; Secure", StringComparison.OrdinalIgnoreCase))
        {
            result += "; Secure";
        }

        return result;
    }

    private static bool IsWorkspaceCookie(string header)
    {
        var separator = header.IndexOf('=');
        if (separator <= 0)
        {
            return false;
        }

        var name = header[..separator].Trim();
        return WorkspaceCookiePrefixes.Any(prefix => name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));
    }
}
