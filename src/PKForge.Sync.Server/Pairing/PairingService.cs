using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using PKForge.Sync.Protocol;
using PKForge.Sync.Server.Accounts;
using PKForge.Sync.Server.Auth;
using PKForge.Sync.Server.Data;
using PKForge.Sync.Server.Http;
using PKForge.Sync.Server.Web;

namespace PKForge.Sync.Server.Pairing;

public sealed class PairingService(SyncDbContext db, AccountService accounts, IOptions<SyncOptions> options, TimeProvider time,
    AccountEvents events)
{
    public static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(5);

    public async Task<PairingCodeResponse> CreateAsync(Guid accountId, CancellationToken ct)
    {
        var now = time.GetUtcNow().UtcDateTime;
        await db.PairingCodes.Where(p => p.AccountId == accountId && (p.Used || p.ExpiresUtc < now)).ExecuteDeleteAsync(ct);

        var code = Secrets.NewPairingCode();
        var expires = now + Lifetime;
        db.PairingCodes.Add(new PairingCode { CodeHash = Secrets.Hash(code), AccountId = accountId, ExpiresUtc = expires });
        await db.SaveChangesAsync(ct);

        var server = options.Value.PublicBaseUrl.TrimEnd('/');
        return new PairingCodeResponse(code, $"pkforge-sync://pair?server={Uri.EscapeDataString(server)}&code={code}", Clock.ToOffset(expires));
    }

    public async Task<DeviceKeyResponse> RedeemAsync(RedeemPairingRequest request, CancellationToken ct)
    {
        var deviceName = AccountService.CheckDeviceName(request.DeviceName);
        var code = Secrets.NormalizePairingCode(request.Code ?? "");
        var hash = Secrets.Hash(code);
        var row = code.Length == Secrets.PairingCodeLength ? await db.PairingCodes.SingleOrDefaultAsync(p => p.CodeHash == hash, ct) : null;
        if (row is null || row.Used || row.ExpiresUtc < time.GetUtcNow().UtcDateTime)
            throw ApiException.BadRequest(ErrorCodes.InvalidPairingCode, "This code is wrong or has expired. Show a new code on your other device.");

        row.Used = true;
        var account = await db.Accounts.AsNoTracking().SingleAsync(a => a.Id == row.AccountId, ct);
        var (device, key) = accounts.AddDevice(account.Id, deviceName);
        await db.SaveChangesAsync(ct);
        events.DevicesChanged(account.Id);
        return new DeviceKeyResponse(account.Id, device.Id, key, account.EmailVerified);
    }
}
