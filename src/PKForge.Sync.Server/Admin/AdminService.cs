using Microsoft.EntityFrameworkCore;
using PKForge.Sync.Protocol;
using PKForge.Sync.Server.Accounts;
using PKForge.Sync.Server.Auth;
using PKForge.Sync.Server.Data;
using PKForge.Sync.Server.Http;
using PKForge.Sync.Server.Web;

namespace PKForge.Sync.Server.Admin;

/// <summary>What a server admin can do to accounts: from the web's Account page and the CLI.</summary>
public sealed class AdminService(SyncDbContext db, AccountService accounts, AccountEvents events)
{
    /// <summary>The users list's cap; self-hosted servers have far fewer accounts.</summary>
    public const int MaxListedUsers = 500;

    /// <summary>Role changes and deletions run one at a time, so two admins can't each remove the other.</summary>
    private static readonly SemaphoreSlim RoleGate = new(1, 1);

    /// <summary>A password for an admin to pass on: 16 URL-safe random characters.</summary>
    public static string NewPassword() => Secrets.NewKey()[..16];

    /// <summary>Accounts, newest first, at most <paramref name="limit"/>.</summary>
    public async Task<AdminUsersResponse> ListUsersAsync(int limit, CancellationToken ct)
    {
        var total = await db.Accounts.CountAsync(ct);
        var users = await Users(db.Accounts.AsNoTracking().OrderByDescending(a => a.CreatedUtc).ThenBy(a => a.NormalizedEmail).Take(limit), ct);
        return new AdminUsersResponse(users, total, limit);
    }

    public async Task<AdminUser> GetUserAsync(Guid id, CancellationToken ct) =>
        (await Users(db.Accounts.AsNoTracking().Where(a => a.Id == id), ct)).SingleOrDefault() ?? throw NoSuchAccount();

    public async Task<AdminUser?> FindByEmailAsync(string? email, CancellationToken ct)
    {
        var normalized = AccountService.NormalizeEmail(email);
        return (await Users(db.Accounts.AsNoTracking().Where(a => a.NormalizedEmail == normalized), ct)).SingleOrDefault();
    }

    /// <summary>Creates a verified account with a generated password, returned once.</summary>
    public async Task<AdminCreateUserResponse> CreateUserAsync(string? email, bool isAdmin, CancellationToken ct)
    {
        var password = NewPassword();
        var account = await accounts.CreateAccountAsync(email, password, isAdmin, emailVerified: true, ct);
        return new AdminCreateUserResponse(await GetUserAsync(account.Id, ct), password);
    }

    /// <summary>Sets a generated password, returned once. Devices stay signed in unless <paramref name="signOutDevices"/>.</summary>
    public async Task<string> ResetPasswordAsync(Guid id, bool signOutDevices, CancellationToken ct)
    {
        await RequireAccountAsync(id, ct);
        var password = NewPassword();
        await accounts.SetPasswordAsync(id, password, ct);
        if (signOutDevices) await SignOutDevicesAsync(id, ct);
        return password;
    }

    /// <summary>Signs out every device and web session of the account; they can sign in again.</summary>
    public async Task SignOutDevicesAsync(Guid id, CancellationToken ct)
    {
        await RequireAccountAsync(id, ct);
        var ids = await db.Devices.Where(d => d.AccountId == id).Select(d => d.Id).ToListAsync(ct);
        await db.Devices.Where(d => ids.Contains(d.Id)).ExecuteDeleteAsync(ct);
        // An unused pairing code would otherwise still add a device.
        await db.PairingCodes.Where(p => p.AccountId == id).ExecuteDeleteAsync(ct);
        events.DevicesRemoved(id, ids);
    }

    public async Task SetAdminAsync(Guid callerId, Guid id, bool isAdmin, CancellationToken ct)
    {
        await RoleGate.WaitAsync(ct);
        try
        {
            var account = await RequireAccountAsync(id, ct);
            if (account.IsAdmin == isAdmin) return;
            if (!isAdmin)
            {
                if (id == callerId)
                    throw ApiException.BadRequest(ErrorCodes.SelfChange, "You can't remove your own admin here.");
                await CheckNotLastAdminAsync(ct);
            }
            await db.Accounts.Where(a => a.Id == id).ExecuteUpdateAsync(s => s.SetProperty(a => a.IsAdmin, isAdmin), ct);
        }
        finally { RoleGate.Release(); }
    }

    /// <summary>Deletes the account and its Bank like a self-delete. <paramref name="confirmEmail"/> must be its email.</summary>
    public async Task DeleteUserAsync(Guid callerId, Guid id, string? confirmEmail, CancellationToken ct)
    {
        await RoleGate.WaitAsync(ct);
        try
        {
            var account = await RequireAccountAsync(id, ct);
            if (id == callerId)
                throw ApiException.BadRequest(ErrorCodes.SelfChange, "Delete your own account from your Account page instead.");
            if (AccountService.NormalizeEmail(confirmEmail) != account.NormalizedEmail)
                throw ApiException.BadRequest(ErrorCodes.InvalidRequest, "Type the account's email to confirm.");
            if (account.IsAdmin) await CheckNotLastAdminAsync(ct);
            await accounts.RemoveAccountAsync(id, ct);
        }
        finally { RoleGate.Release(); }
    }

    private async Task CheckNotLastAdminAsync(CancellationToken ct)
    {
        if (await db.Accounts.CountAsync(a => a.IsAdmin, ct) <= 1)
            throw new ApiException(StatusCodes.Status409Conflict, ErrorCodes.LastAdmin, "The server needs at least one admin.");
    }

    private async Task<Account> RequireAccountAsync(Guid id, CancellationToken ct) =>
        await db.Accounts.AsNoTracking().SingleOrDefaultAsync(a => a.Id == id, ct) ?? throw NoSuchAccount();

    private static ApiException NoSuchAccount() => ApiException.NotFound("No such account.");

    private async Task<List<AdminUser>> Users(IQueryable<Account> accountsQuery, CancellationToken ct)
    {
        var rows = await accountsQuery.Select(a => new
        {
            a.Id, a.Email, a.IsAdmin, a.EmailVerified, a.CreatedUtc,
            Devices = db.Devices.Count(d => d.AccountId == a.Id),
            Mons = db.Items.Count(i => i.AccountId == a.Id && i.Kind == ItemKinds.Mon && !i.Deleted),
            LastActive = db.Devices.Where(d => d.AccountId == a.Id).Max(d => d.LastSyncUtc),
        }).ToListAsync(ct);
        return rows.Select(r => new AdminUser(r.Id, r.Email, r.IsAdmin, r.EmailVerified, r.Devices, r.Mons,
            Clock.ToOffset(r.CreatedUtc), r.LastActive is { } last ? Clock.ToOffset(last) : null)).ToList();
    }
}
