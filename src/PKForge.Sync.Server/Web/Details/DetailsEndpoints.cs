using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using PKForge.Sync.Protocol;
using PKForge.Sync.Server.Auth;
using PKForge.Sync.Server.Blobs;
using PKForge.Sync.Server.Data;
using PKForge.Sync.Server.Http;

namespace PKForge.Sync.Server.Web.Details;

public static class DetailsEndpoints
{
    public static void MapDetailsEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/v1/web/mons/{id:guid}/details", async (Guid id, ClaimsPrincipal user, SyncDbContext db, BlobService blobs,
            DetailsCache cache, CancellationToken ct) =>
        {
            var accountId = user.AccountId();
            var key = id.ToString("D");
            var blob = await db.Items.AsNoTracking()
                .Where(i => i.AccountId == accountId && i.Kind == ItemKinds.Mon && i.Id == key && !i.Deleted)
                .Select(i => i.Blob).SingleOrDefaultAsync(ct);
            if (blob is null) throw ApiException.NotFound("No such Pokémon.");
            if (cache.Get(blob) is { } cached) return cached;
            var data = await blobs.GetAsync(accountId, blob, ct);
            if (data is null) return new MonDetails { Available = false };
            var details = MonDetailsReader.Read(data);
            cache.Set(blob, details);
            return details;
        }).RequireAuthorization().AddEndpointFilter<RequireVerifiedEmailFilter>();
    }
}
