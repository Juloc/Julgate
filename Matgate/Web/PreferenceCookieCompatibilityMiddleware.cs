namespace Matgate.Web;

public sealed class PreferenceCookieCompatibilityMiddleware(RequestDelegate next)
{
    private static readonly (string Legacy, string Current)[] CookieNames =
    [
        ("Matgate.Language", "Julgate.Language"),
        ("Matgate.Theme", "Julgate.Theme"),
        ("Matgate.RememberLogin", "Julgate.RememberLogin")
    ];

    public async Task InvokeAsync(HttpContext context)
    {
        var requestCookieHeader = context.Request.Headers.Cookie.ToString();
        var legacyValues = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var (legacy, current) in CookieNames)
        {
            var legacyValue = ReadCookie(requestCookieHeader, legacy);
            var currentValue = ReadCookie(requestCookieHeader, current);
            if (!string.IsNullOrEmpty(legacyValue))
            {
                legacyValues[legacy] = legacyValue;
            }

            if (string.IsNullOrEmpty(legacyValue) && !string.IsNullOrEmpty(currentValue))
            {
                requestCookieHeader = AppendRequestCookie(requestCookieHeader, legacy, currentValue);
            }
        }

        context.Request.Headers.Cookie = requestCookieHeader;
        context.Response.OnStarting(() =>
        {
            var existing = context.Response.Headers.SetCookie.ToArray();
            var additions = new List<string>();

            foreach (var (legacy, current) in CookieNames)
            {
                var migratedFromResponse = false;
                foreach (var cookie in existing)
                {
                    if (!cookie.StartsWith(legacy + "=", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    additions.Add(current + cookie[legacy.Length..]);
                    additions.Add(DeleteCookie(legacy, context.Request.IsHttps));
                    migratedFromResponse = true;
                }

                if (!migratedFromResponse
                    && legacyValues.TryGetValue(legacy, out var legacyValue)
                    && string.IsNullOrEmpty(ReadCookie(requestCookieHeader, current)))
                {
                    additions.Add(CreatePreferenceCookie(current, legacyValue, context.Request.IsHttps));
                    additions.Add(DeleteCookie(legacy, context.Request.IsHttps));
                }
            }

            foreach (var addition in additions.Distinct(StringComparer.Ordinal))
            {
                context.Response.Headers.Append("Set-Cookie", addition);
            }

            return Task.CompletedTask;
        });

        await next(context);
    }

    private static string AppendRequestCookie(string header, string name, string value)
    {
        var prefix = string.IsNullOrWhiteSpace(header) ? "" : header.TrimEnd() + "; ";
        return prefix + name + "=" + value;
    }

    private static string? ReadCookie(string header, string name)
    {
        foreach (var part in header.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var separator = part.IndexOf('=');
            if (separator <= 0 || !string.Equals(part[..separator], name, StringComparison.Ordinal))
            {
                continue;
            }

            return part[(separator + 1)..];
        }

        return null;
    }

    private static string CreatePreferenceCookie(string name, string value, bool secure)
    {
        return $"{name}={value}; path=/; max-age=31536000; samesite=strict{(secure ? "; secure" : "")}";
    }

    private static string DeleteCookie(string name, bool secure)
    {
        return $"{name}=; path=/; expires=Thu, 01 Jan 1970 00:00:00 GMT; max-age=0; samesite=strict{(secure ? "; secure" : "")}";
    }
}
