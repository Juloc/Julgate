using System.Security.Claims;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;

namespace Matgate.Web;

internal enum RequestRateLimitBucket
{
    Login,
    Write,
    Read
}

internal sealed record RequestRateLimitDecision(
    RequestRateLimitBucket Bucket,
    string PartitionKey,
    int PermitLimit);

internal static class RequestRateLimitPolicy
{
    internal static RequestRateLimitDecision Classify(
        HttpContext context,
        int loginPermitLimit,
        int writePermitLimit,
        int readPermitLimit)
    {
        var request = context.Request;
        var isLoginAttempt = HttpMethods.IsPost(request.Method)
            && string.Equals(request.Path.Value, "/login", StringComparison.OrdinalIgnoreCase);
        var isWrite = HttpMethods.IsPost(request.Method)
            || HttpMethods.IsPut(request.Method)
            || HttpMethods.IsPatch(request.Method)
            || HttpMethods.IsDelete(request.Method);
        var remoteAddress = context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        var userId = context.User.FindFirstValue(ClaimTypes.NameIdentifier);

        if (isLoginAttempt)
        {
            return new RequestRateLimitDecision(
                RequestRateLimitBucket.Login,
                $"login:ip:{remoteAddress}",
                loginPermitLimit);
        }

        var callerKey = string.IsNullOrWhiteSpace(userId)
            ? $"ip:{remoteAddress}"
            : $"user:{userId}";

        return isWrite
            ? new RequestRateLimitDecision(
                RequestRateLimitBucket.Write,
                $"write:{callerKey}",
                writePermitLimit)
            : new RequestRateLimitDecision(
                RequestRateLimitBucket.Read,
                $"read:{callerKey}",
                readPermitLimit);
    }

    internal static RateLimitPartition<string> CreatePartition(
        HttpContext context,
        int loginPermitLimit,
        int writePermitLimit,
        int readPermitLimit)
    {
        var decision = Classify(context, loginPermitLimit, writePermitLimit, readPermitLimit);
        return RateLimitPartition.GetFixedWindowLimiter(
            decision.PartitionKey,
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = decision.PermitLimit,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0,
                QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                AutoReplenishment = true
            });
    }
}
