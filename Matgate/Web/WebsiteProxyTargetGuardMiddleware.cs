using System.Net;
using Matgate.Models;
using Matgate.Services;

namespace Matgate.Web;

public sealed class WebsiteProxyTargetGuardMiddleware
{
    private readonly RequestDelegate _next;
    private readonly JsonDataStore _store;
    private readonly bool _websiteProxyEnabled;

    public WebsiteProxyTargetGuardMiddleware(
        RequestDelegate next,
        IConfiguration configuration,
        JsonDataStore store)
    {
        _next = next;
        _store = store;
        _websiteProxyEnabled = ReadBoolean(configuration, "JULGATE_ENABLE_WEBSITE_PROXY", false);
    }

    public async Task InvokeAsync(HttpContext context)
    {
        if (!_websiteProxyEnabled
            || !TryReadServerId(context.Request.Path, out var serverId))
        {
            await _next(context);
            return;
        }

        var server = await _store.FindServerByIdAsync(serverId, context.RequestAborted);
        if (server is null || server.Protocol != ServerProtocol.Website)
        {
            await _next(context);
            return;
        }

        var normalized = ServerEndpoint.NormalizeWebsiteUrl(server.WebsiteUrl, server.Host);
        if (!Uri.TryCreate(normalized, UriKind.Absolute, out var target)
            || !await IsAllowedAsync(target, context.RequestAborted))
        {
            context.Response.StatusCode = StatusCodes.Status400BadRequest;
            context.Response.ContentType = "text/plain; charset=utf-8";
            await context.Response.WriteAsync(
                "Website proxy targets must use an explicit, permitted IP address. DNS names are blocked to prevent DNS rebinding.",
                context.RequestAborted);
            return;
        }

        await _next(context);
    }

    internal static Task<bool> IsAllowedAsync(Uri target, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(
            IPAddress.TryParse(target.Host, out var literalAddress)
            && !IsDisallowedAddress(literalAddress));
    }

    internal static bool IsDisallowedAddress(IPAddress address)
    {
        if (IPAddress.IsLoopback(address)
            || address.Equals(IPAddress.Any)
            || address.Equals(IPAddress.IPv6Any)
            || address.Equals(IPAddress.None)
            || address.Equals(IPAddress.IPv6None)
            || address.IsIPv6LinkLocal
            || address.IsIPv6SiteLocal
            || address.IsIPv6Multicast)
        {
            return true;
        }

        var bytes = address.MapToIPv6().GetAddressBytes();
        var isIpv4Mapped = bytes.AsSpan(0, 10).SequenceEqual(new byte[10])
            && bytes[10] == 0xff
            && bytes[11] == 0xff;
        if (isIpv4Mapped)
        {
            var ipv4 = bytes.AsSpan(12, 4);
            if (ipv4[0] == 0
                || (ipv4[0] == 169 && ipv4[1] == 254)
                || ipv4[0] >= 224)
            {
                return true;
            }

            if (ipv4.SequenceEqual(new byte[] { 100, 100, 100, 200 })
                || ipv4.SequenceEqual(new byte[] { 168, 63, 129, 16 }))
            {
                return true;
            }
        }

        return address.Equals(IPAddress.Parse("fd00:ec2::254"));
    }

    private static bool TryReadServerId(PathString path, out Guid serverId)
    {
        serverId = Guid.Empty;
        var segments = (path.Value ?? "")
            .Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return segments.Length >= 2
            && string.Equals(segments[0], "website", StringComparison.OrdinalIgnoreCase)
            && Guid.TryParse(segments[1], out serverId);
    }

    private static bool ReadBoolean(IConfiguration configuration, string name, bool fallback)
    {
        return bool.TryParse(
                Environment.GetEnvironmentVariable(name)
                ?? configuration[$"Julgate:{name["JULGATE_".Length..]}"],
                out var value)
            ? value
            : fallback;
    }
}
