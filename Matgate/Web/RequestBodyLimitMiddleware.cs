using Matgate.Services;

namespace Matgate.Web;

public sealed class RequestBodyLimitMiddleware
{
    private readonly RequestDelegate _next;
    private readonly long _maxRequestBytes;

    public RequestBodyLimitMiddleware(RequestDelegate next, IConfiguration configuration)
    {
        _next = next;
        _maxRequestBytes = ReadPositiveLong(configuration, "JULGATE_MAX_REQUEST_BODY_BYTES", 512L * 1024L * 1024L);
    }

    public async Task InvokeAsync(HttpContext context)
    {
        if (!MayHaveBody(context.Request.Method))
        {
            await _next(context);
            return;
        }

        if (context.Request.ContentLength is > 0 && context.Request.ContentLength > _maxRequestBytes)
        {
            await RejectAsync(context);
            return;
        }

        var originalBody = context.Request.Body;
        await using var bounded = new BoundedReadStream(
            originalBody,
            _maxRequestBytes,
            countAgainstArchiveBudget: false,
            leaveOpen: true);
        context.Request.Body = bounded;

        try
        {
            await _next(context);
        }
        catch (FileTransferLimitExceededException) when (!context.Response.HasStarted)
        {
            await RejectAsync(context);
        }
        finally
        {
            context.Request.Body = originalBody;
        }
    }

    private static bool MayHaveBody(string method)
    {
        return HttpMethods.IsPost(method)
            || HttpMethods.IsPut(method)
            || HttpMethods.IsPatch(method)
            || HttpMethods.IsDelete(method);
    }

    private static async Task RejectAsync(HttpContext context)
    {
        context.Response.Clear();
        context.Response.StatusCode = StatusCodes.Status413PayloadTooLarge;
        context.Response.ContentType = "application/problem+json; charset=utf-8";
        await context.Response.WriteAsJsonAsync(
            new
            {
                type = "https://httpstatuses.com/413",
                title = "Payload too large",
                status = StatusCodes.Status413PayloadTooLarge
            },
            context.RequestAborted);
    }

    private static long ReadPositiveLong(IConfiguration configuration, string name, long fallback)
    {
        var raw = Environment.GetEnvironmentVariable(name)
            ?? configuration[$"Julgate:{name["JULGATE_".Length..]}"];
        return long.TryParse(raw, out var value) && value > 0 ? value : fallback;
    }
}
