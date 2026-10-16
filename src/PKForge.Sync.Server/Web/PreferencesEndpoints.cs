using System.Security.Claims;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using PKForge.Sync.Protocol;
using PKForge.Sync.Server.Auth;
using PKForge.Sync.Server.Data;
using PKForge.Sync.Server.Http;

namespace PKForge.Sync.Server.Web;

public static class PreferencesEndpoints
{
    public const int MaxBytes = 4096;

    public static void MapPreferencesEndpoints(this IEndpointRouteBuilder app)
    {
        var prefs = app.MapGroup("/v1/account/preferences").RequireAuthorization();

        prefs.MapGet("", async (ClaimsPrincipal user, SyncDbContext db, CancellationToken ct) =>
        {
            var id = user.AccountId();
            var json = await db.Preferences.AsNoTracking().Where(p => p.AccountId == id).Select(p => p.Json).SingleOrDefaultAsync(ct);
            return Results.Text(json ?? "{}", "application/json");
        });

        prefs.MapPut("", async (HttpRequest request, ClaimsPrincipal user, SyncDbContext db, TimeProvider time, AccountEvents events, CancellationToken ct) =>
        {
            var tooBig = ApiException.BadRequest(ErrorCodes.InvalidRequest, $"Preferences are at most {MaxBytes} bytes.");
            if (request.ContentLength > MaxBytes) throw tooBig;
            // Never buffer more than the cap (+1 to notice an overrun), whatever the client sends.
            var buffer = new byte[MaxBytes + 1];
            var length = 0;
            while (length < buffer.Length)
            {
                var read = await request.Body.ReadAsync(buffer.AsMemory(length), ct);
                if (read == 0) break;
                length += read;
            }
            if (length > MaxBytes) throw tooBig;
            JsonElement value;
            try
            {
                using var doc = JsonDocument.Parse(buffer.AsMemory(0, length));
                value = doc.RootElement.Clone();
            }
            catch (JsonException) { throw ApiException.BadRequest(ErrorCodes.InvalidRequest, "Preferences are a JSON object."); }
            if (value.ValueKind != JsonValueKind.Object) throw ApiException.BadRequest(ErrorCodes.InvalidRequest, "Preferences are a JSON object.");

            var id = user.AccountId();
            var row = await db.Preferences.SingleOrDefaultAsync(p => p.AccountId == id, ct);
            if (row is null) db.Preferences.Add(row = new AccountPreferences { AccountId = id, Json = "" });
            row.Json = value.GetRawText();
            row.UpdatedUtc = time.GetUtcNow().UtcDateTime;
            await db.SaveChangesAsync(ct);
            // Every open stream of the account, including other tabs of this browser (they share one device).
            events.PreferencesChanged(id);
            return Results.NoContent();
        });
    }
}
