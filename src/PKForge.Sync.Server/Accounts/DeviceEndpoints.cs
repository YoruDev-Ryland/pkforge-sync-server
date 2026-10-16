using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using PKForge.Sync.Protocol;
using PKForge.Sync.Server.Auth;
using PKForge.Sync.Server.Data;
using PKForge.Sync.Server.Http;
using PKForge.Sync.Server.Web;

namespace PKForge.Sync.Server.Accounts;

public static class DeviceEndpoints
{
    public static void MapDeviceEndpoints(this IEndpointRouteBuilder app)
    {
        var devices = app.MapGroup("/v1/devices").RequireAuthorization();

        devices.MapGet("", async (ClaimsPrincipal user, SyncDbContext db, CancellationToken ct) =>
        {
            var accountId = user.AccountId();
            var current = user.DeviceId();
            var rows = await db.Devices.AsNoTracking().Where(d => d.AccountId == accountId).OrderBy(d => d.CreatedUtc).ToListAsync(ct);
            return rows.Select(d => new DeviceInfo(d.Id, d.Name, Clock.ToOffset(d.CreatedUtc),
                d.LastSyncUtc is { } synced ? Clock.ToOffset(synced) : null, d.Id == current)).ToList();
        });

        devices.MapDelete("/{id:guid}", async (Guid id, ClaimsPrincipal user, SyncDbContext db, AccountEvents events, CancellationToken ct) =>
        {
            var accountId = user.AccountId();
            var removed = await db.Devices.Where(d => d.Id == id && d.AccountId == accountId).ExecuteDeleteAsync(ct);
            if (removed == 0) throw ApiException.NotFound("No such device.");
            events.DevicesRemoved(accountId, [id]);
            return Results.NoContent();
        });
    }
}
