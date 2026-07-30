using System.Text;

namespace Matgate.Web;

public sealed class Ae01ThemeMiddleware(RequestDelegate next)
{
    private const string StylesheetMarkup = "<link rel=\"stylesheet\" href=\"/assets/julgate-ae01.css\">";

    public async Task InvokeAsync(HttpContext context)
    {
        if (!ShouldTransform(context.Request))
        {
            await next(context);
            return;
        }

        var originalBody = context.Response.Body;
        await using var buffer = new MemoryStream();
        context.Response.Body = buffer;

        try
        {
            await next(context);
            buffer.Position = 0;

            if (!IsHtmlResponse(context.Response))
            {
                await buffer.CopyToAsync(originalBody, context.RequestAborted);
                return;
            }

            using var reader = new StreamReader(
                buffer,
                Encoding.UTF8,
                detectEncodingFromByteOrderMarks: true,
                bufferSize: 4096,
                leaveOpen: true);
            var html = await reader.ReadToEndAsync(context.RequestAborted);
            html = ApplyBranding(html);

            if (!html.Contains("/assets/julgate-ae01.css", StringComparison.OrdinalIgnoreCase))
            {
                html = html.Replace("</head>", $"{StylesheetMarkup}</head>", StringComparison.OrdinalIgnoreCase);
            }

            var output = Encoding.UTF8.GetBytes(html);
            context.Response.ContentLength = output.Length;

            if (!HttpMethods.IsHead(context.Request.Method))
            {
                await originalBody.WriteAsync(output, context.RequestAborted);
            }
        }
        finally
        {
            context.Response.Body = originalBody;
        }
    }

    private static bool ShouldTransform(HttpRequest request)
    {
        if (!HttpMethods.IsGet(request.Method) && !HttpMethods.IsHead(request.Method))
        {
            return false;
        }

        var path = request.Path.Value ?? "/";
        if (path.Contains("/download", StringComparison.OrdinalIgnoreCase)
            || path.Contains("/raw", StringComparison.OrdinalIgnoreCase)
            || path.Contains("/preview", StringComparison.OrdinalIgnoreCase)
            || path.StartsWith("/api/", StringComparison.OrdinalIgnoreCase)
            || path.StartsWith("/guacamole", StringComparison.OrdinalIgnoreCase)
            || path.StartsWith("/website", StringComparison.OrdinalIgnoreCase)
            || path.StartsWith("/assets/", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return path is "/" or "/login" or "/sessions" or "/account" or "/tools" or "/about"
            || path.StartsWith("/admin", StringComparison.OrdinalIgnoreCase)
            || path.StartsWith("/workspaces", StringComparison.OrdinalIgnoreCase)
            || path.StartsWith("/workspace/", StringComparison.OrdinalIgnoreCase)
            || path.StartsWith("/w/", StringComparison.OrdinalIgnoreCase)
            || path.StartsWith("/connect/", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsHtmlResponse(HttpResponse response)
    {
        return response.StatusCode is >= 200 and < 400
            && response.ContentType?.StartsWith("text/html", StringComparison.OrdinalIgnoreCase) == true;
    }

    private static string ApplyBranding(string html)
    {
        return html
            .Replace(">Matgate<", ">Julgate<", StringComparison.Ordinal)
            .Replace(">MATGATE ", ">JULGATE ", StringComparison.Ordinal)
            .Replace(" - Matgate</title>", " - Julgate</title>", StringComparison.Ordinal)
            .Replace("content=\"Matgate\"", "content=\"Julgate\"", StringComparison.Ordinal)
            .Replace("content=\"MATGATE\"", "content=\"JULGATE\"", StringComparison.Ordinal)
            .Replace("Matgate bereitet die Sitzung vor.", "Julgate bereitet die Sitzung vor.", StringComparison.Ordinal)
            .Replace("Back to Matgate", "Back to Julgate", StringComparison.Ordinal)
            .Replace("Zurueck zu Matgate", "Zurueck zu Julgate", StringComparison.Ordinal);
    }
}
