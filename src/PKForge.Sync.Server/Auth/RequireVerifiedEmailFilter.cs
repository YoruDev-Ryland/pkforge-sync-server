using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using PKForge.Sync.Protocol;
using PKForge.Sync.Server.Data;
using PKForge.Sync.Server.Http;

namespace PKForge.Sync.Server.Auth;

/// <summary>When the server requires verified emails, blocks syncing, blobs and pairing until verified.</summary>
public sealed class RequireVerifiedEmailFilter : IEndpointFilter
{
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var http = context.HttpContext;
        if (http.RequestServices.GetRequiredService<IOptions<SyncOptions>>().Value.RequireEmailVerification)
        {
            var accountId = http.User.AccountId();
            var db = http.RequestServices.GetRequiredService<SyncDbContext>();
            if (!await db.Accounts.AnyAsync(a => a.Id == accountId && a.EmailVerified, http.RequestAborted))
                throw new ApiException(StatusCodes.Status403Forbidden, ErrorCodes.EmailNotVerified, "Confirm your email address to start syncing.");
        }
        return await next(context);
    }
}
