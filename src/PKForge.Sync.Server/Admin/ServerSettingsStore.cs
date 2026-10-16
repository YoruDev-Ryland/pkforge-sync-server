using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using PKForge.Sync.Server.Data;

namespace PKForge.Sync.Server.Admin;

/// <summary>
/// Settings an admin can change from the web. Each starts at its configured value (environment) until an admin
/// sets it; from then on the stored value wins.
/// </summary>
public sealed class ServerSettingsStore(SyncDbContext db, IOptions<SyncOptions> options, TimeProvider time)
{
    public const string AllowRegistrationKey = "AllowRegistration";

    /// <summary>Whether anyone may create an account (the admin's choice, else ALLOW_REGISTRATION).</summary>
    public async Task<bool> AllowRegistrationAsync(CancellationToken ct)
    {
        var stored = await db.ServerSettings.AsNoTracking()
            .Where(s => s.Key == AllowRegistrationKey).Select(s => s.Value).SingleOrDefaultAsync(ct);
        return stored is null ? options.Value.AllowRegistration : bool.Parse(stored);
    }

    /// <summary>Sign-ups are open when allowed, and always for a server's first account.</summary>
    public async Task<bool> RegistrationOpenAsync(CancellationToken ct) =>
        await AllowRegistrationAsync(ct) || !await db.Accounts.AnyAsync(ct);

    public async Task SetAllowRegistrationAsync(bool allow, CancellationToken ct)
    {
        var row = await db.ServerSettings.SingleOrDefaultAsync(s => s.Key == AllowRegistrationKey, ct);
        if (row is null) db.ServerSettings.Add(row = new ServerSetting { Key = AllowRegistrationKey, Value = "" });
        row.Value = allow ? "true" : "false";
        row.UpdatedUtc = time.GetUtcNow().UtcDateTime;
        await db.SaveChangesAsync(ct);
    }
}
