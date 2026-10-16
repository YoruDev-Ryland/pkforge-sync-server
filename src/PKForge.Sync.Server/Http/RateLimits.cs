using System.Threading.RateLimiting;
using Microsoft.Extensions.Options;
using PKForge.Sync.Protocol;

namespace PKForge.Sync.Server.Http;

public static class RateLimits
{
    /// <summary>Login, sign-up, pairing redemption and password reset: per client IP, per minute.</summary>
    public const string Auth = "auth";

    public static IServiceCollection AddSyncRateLimiting(this IServiceCollection services) => services.AddRateLimiter(o =>
    {
        o.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
        o.OnRejected = (context, _) => new ValueTask(ApiErrors.Write(context.HttpContext,
            StatusCodes.Status429TooManyRequests, ErrorCodes.RateLimited, "Too many attempts. Try again in a minute."));
        o.AddPolicy(Auth, context =>
        {
            var limit = context.RequestServices.GetRequiredService<IOptions<SyncOptions>>().Value.AuthRequestsPerMinute;
            return RateLimitPartition.GetFixedWindowLimiter(
                context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                _ => new FixedWindowRateLimiterOptions { PermitLimit = limit, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 });
        });
    });
}
