using System.Security.Claims;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using PKForge.Sync.Protocol;
using PKForge.Sync.Server.Auth;
using PKForge.Sync.Server.Data;

namespace PKForge.Sync.Server.Web;

/// <summary>
/// Recently released Pokémon, for undoing a release from the web Bank. Restoring needs no endpoint of its own:
/// the client pushes the mon again on top of its tombstone, and the kept blob makes the push valid.
/// </summary>
public static class ReleasedEndpoints
{
    public const int MaxItems = 500;

    public static void MapReleasedEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/v1/bank/released", async (ClaimsPrincipal user, SyncDbContext db, IOptions<SyncOptions> options, TimeProvider time,
            CancellationToken ct) =>
        {
            var days = options.Value.ReleasedRetentionDays;
            if (days <= 0) return new ReleasedResponse(0, []);
            var accountId = user.AccountId();
            var cutoff = time.GetUtcNow().UtcDateTime.AddDays(-days);
            var rows = await db.Items.AsNoTracking()
                .Where(i => i.AccountId == accountId && i.Kind == ItemKinds.Mon && i.Deleted && i.ReleasedValue != null && i.UpdatedUtc > cutoff)
                .OrderByDescending(i => i.Version)
                .Take(MaxItems)
                .Select(i => new { i.Id, i.Version, i.UpdatedUtc, i.ReleasedValue })
                .ToListAsync(ct);
            return new ReleasedResponse(days, rows.Select(r => new ReleasedMon(r.Id, r.Version, Clock.ToOffset(r.UpdatedUtc),
                JsonSerializer.Deserialize<JsonElement>(r.ReleasedValue!))).ToList());
        }).RequireAuthorization().AddEndpointFilter<RequireVerifiedEmailFilter>();
    }
}
