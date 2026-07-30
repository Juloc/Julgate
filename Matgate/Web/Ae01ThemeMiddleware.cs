using System.Text;

namespace Matgate.Web;

public sealed class Ae01ThemeMiddleware(RequestDelegate next)
{
    private const string StylesheetMarkup = "<link rel=\"stylesheet\" href=\"/assets/julgate-ae01.css\">";
    private const string StorageMigrationScript = """
        <script id="julgate-storage-migration">
        (() => {
          const mappings = [
            ['matgate.workspace.tabs.v2', 'julgate.workspace.tabs.v2'],
            ['matgate.shell.tabs.v1', 'julgate.shell.tabs.v1'],
            ['matgate.tab.order.v1', 'julgate.tab.order.v1'],
            ['matgate.home.browser.v1', 'julgate.home.browser.v1'],
            ['matgate.app.boot.signature.v1', 'julgate.app.boot.signature.v1'],
            ['matgate.pointer.mode.v2', 'julgate.pointer.mode.v2'],
            ['matgate.display.res.v3', 'julgate.display.res.v3'],
            ['matgate.view.mode.v1', 'julgate.view.mode.v1']
          ];
          const migrate = storage => {
            try {
              mappings.forEach(([legacy, current]) => {
                if (storage.getItem(current) === null && storage.getItem(legacy) !== null) {
                  storage.setItem(current, storage.getItem(legacy));
                }
                storage.removeItem(legacy);
              });
              for (let index = storage.length - 1; index >= 0; index -= 1) {
                const key = storage.key(index);
                if (!key || !key.startsWith('matgate.workspace.panel.')) continue;
                const current = `julgate.workspace.panel.${key.substring('matgate.workspace.panel.'.length)}`;
                if (storage.getItem(current) === null) storage.setItem(current, storage.getItem(key));
                storage.removeItem(key);
              }
            } catch { }
          };
          migrate(window.localStorage);
          migrate(window.sessionStorage);
        })();
        </script>
        """;

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

            if (!IsTransformableResponse(context.Response))
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
            var outputText = ApplyBranding(await reader.ReadToEndAsync(context.RequestAborted));

            if (IsHtmlResponse(context.Response))
            {
                if (!outputText.Contains("julgate-storage-migration", StringComparison.OrdinalIgnoreCase))
                {
                    outputText = outputText.Replace(
                        "<head>",
                        $"<head>{StorageMigrationScript}",
                        StringComparison.OrdinalIgnoreCase);
                }

                if (!outputText.Contains("/assets/julgate-ae01.css", StringComparison.OrdinalIgnoreCase))
                {
                    outputText = outputText.Replace(
                        "</head>",
                        $"{StylesheetMarkup}</head>",
                        StringComparison.OrdinalIgnoreCase);
                }
            }

            var output = Encoding.UTF8.GetBytes(outputText);
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
        if (string.Equals(path, "/manifest.webmanifest", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

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

    private static bool IsTransformableResponse(HttpResponse response)
    {
        return response.StatusCode is >= 200 and < 400
            && (IsHtmlResponse(response)
                || response.ContentType?.StartsWith("application/manifest+json", StringComparison.OrdinalIgnoreCase) == true);
    }

    private static bool IsHtmlResponse(HttpResponse response)
    {
        return response.ContentType?.StartsWith("text/html", StringComparison.OrdinalIgnoreCase) == true;
    }

    internal static string ApplyBranding(string content)
    {
        return content
            .Replace("Matgate", "Julgate", StringComparison.Ordinal)
            .Replace("MATGATE", "JULGATE", StringComparison.Ordinal)
            .Replace("matgate.workspace.tabs.v2", "julgate.workspace.tabs.v2", StringComparison.Ordinal)
            .Replace("matgate.shell.tabs.v1", "julgate.shell.tabs.v1", StringComparison.Ordinal)
            .Replace("matgate.tab.order.v1", "julgate.tab.order.v1", StringComparison.Ordinal)
            .Replace("matgate.home.browser.v1", "julgate.home.browser.v1", StringComparison.Ordinal)
            .Replace("matgate.app.boot.signature.v1", "julgate.app.boot.signature.v1", StringComparison.Ordinal)
            .Replace("matgate.workspace.panel.", "julgate.workspace.panel.", StringComparison.Ordinal)
            .Replace("matgate.pointer.mode.v2", "julgate.pointer.mode.v2", StringComparison.Ordinal)
            .Replace("matgate.display.res.v3", "julgate.display.res.v3", StringComparison.Ordinal)
            .Replace("matgate.view.mode.v1", "julgate.view.mode.v1", StringComparison.Ordinal)
            .Replace("matgate-archive", "julgate-archive", StringComparison.Ordinal);
    }
}
