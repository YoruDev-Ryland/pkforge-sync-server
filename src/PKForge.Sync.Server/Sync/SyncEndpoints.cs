using System.Security.Claims;
using PKForge.Sync.Protocol;
using PKForge.Sync.Server.Auth;
using PKForge.Sync.Server.Web;

namespace PKForge.Sync.Server.Sync;

public static class SyncEndpoints
{
    public static void MapSyncEndpoints(this IEndpointRouteBuilder app)
    {
        var sync = app.MapGroup("/v1/sync").RequireAuthorization().AddEndpointFilter<RequireVerifiedEmailFilter>();

        sync.MapPost("/push", (PushRequest request, ClaimsPrincipal user, SyncService service, CancellationToken ct) =>
            service.PushAsync(user.AccountId(), user.DeviceId(), request, ct));

        sync.MapGet("/changes", (long since, string? epoch, int? limit, ClaimsPrincipal user, SyncService service, CancellationToken ct) =>
            service.ChangesAsync(user.AccountId(), user.DeviceId(), since, epoch, limit, ct));

        // Change notifications for the web Bank (server-sent events).
        sync.MapGet("/events", EventStream.RunAsync);
    }
}
