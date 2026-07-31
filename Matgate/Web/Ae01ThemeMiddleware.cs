using System.Text;

namespace Matgate.Web;

public sealed class Ae01ThemeMiddleware(RequestDelegate next)
{
    private const string StylesheetMarkup = "<link rel=\"stylesheet\" href=\"/assets/julgate-ae01.css\">";
    private const string ShellEnhancementMarkup = "<script src=\"/assets/julgate-shell-enhancements.js\" defer></script>";
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
                const legacyValue = storage.getItem(legacy);
                if (storage.getItem(current) === null && legacyValue !== null) {
                  storage.setItem(current, legacyValue);
                }
                storage.removeItem(legacy);
              });

              for (let index = storage.length - 1; index >= 0; index -= 1) {
                const key = storage.key(index);
                if (!key || !key.startsWith('matgate.workspace.panel.')) continue;
                const current = `julgate.workspace.panel.${key.substring('matgate.workspace.panel.'.length)}`;
                const legacyValue = storage.getItem(key);
                if (storage.getItem(current) === null && legacyValue !== null) {
                  storage.setItem(current, legacyValue);
                }
                storage.removeItem(key);
              }
            } catch {
              // Storage can be unavailable in restricted browser contexts.
            }
          };

          const synchronizeProductMark = root => {
            const elements = [];
            if (root instanceof Element && root.matches('.brand-word')) elements.push(root);
            if (root && typeof root.querySelectorAll === 'function') {
              elements.push(...root.querySelectorAll('.brand-word'));
            }

            elements.forEach(element => {
              if ((element.textContent || '').replace(/\s+/g, '').toUpperCase() === 'MATGATE') {
                element.innerHTML = '<span>JUL</span>GATE';
              }
            });

            if (document.title.includes('Matgate') || document.title.includes('MATGATE')) {
              document.title = document.title
                .replaceAll('MATGATE', 'JULGATE')
                .replaceAll('Matgate', 'Julgate');
            }
          };

          migrate(window.localStorage);
          migrate(window.sessionStorage);

          const start = () => {
            synchronizeProductMark(document);

            // The legacy shell can replace its header once during startup. Observe only
            // newly inserted elements instead of repeatedly scanning the complete DOM.
            const observer = new MutationObserver(records => {
              records.forEach(record => record.addedNodes.forEach(node => {
                if (node.nodeType === Node.ELEMENT_NODE) synchronizeProductMark(node);
              }));
            });
            observer.observe(document.documentElement, { subtree: true, childList: true });
            window.setTimeout(() => observer.disconnect(), 5000);
          };

          if (document.readyState === 'loading') {
            document.addEventListener('DOMContentLoaded', start, { once: true });
          } else {
            start();
          }
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

                if (!outputText.Contains("/assets/julgate-shell-enhancements.js", StringComparison.OrdinalIgnoreCase))
                {
                    outputText = outputText.Replace(
                        "</head>",
                        $"{ShellEnhancementMarkup}</head>",
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
            .Replace(
                "<span class=\"brand-word\"><span>MAT</span>GATE</span>",
                "<span class=\"brand-word\"><span>JUL</span>GATE</span>",
                StringComparison.Ordinal)
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
