using System.Net.Mail;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using PKForge.Sync.Protocol;
using PKForge.Sync.Server.Admin;
using PKForge.Sync.Server.Auth;
using PKForge.Sync.Server.Blobs;
using PKForge.Sync.Server.Data;
using PKForge.Sync.Server.Email;
using PKForge.Sync.Server.Http;
using PKForge.Sync.Server.Sync;
using PKForge.Sync.Server.Web;

namespace PKForge.Sync.Server.Accounts;

public sealed class AccountService(
    SyncDbContext db, IOptions<SyncOptions> options, TimeProvider time, IEmailSender email, ILogger<AccountService> log,
    IBlobStore blobs, AccountLocks locks, AccountEvents events, ServerSettingsStore settings)
{
    public const int MinPasswordLength = 8;
    private static readonly PasswordHasher<Account> Hasher = new();

    private DateTime Now => time.GetUtcNow().UtcDateTime;

    public async Task<DeviceKeyResponse> RegisterAsync(RegisterRequest request, CancellationToken ct)
    {
        var deviceName = CheckDeviceName(request.DeviceName);
        var first = !await db.Accounts.AnyAsync(ct);
        if (!first && !await settings.AllowRegistrationAsync(ct))
            throw new ApiException(StatusCodes.Status403Forbidden, ErrorCodes.RegistrationClosed, "This server does not accept new accounts.");

        var account = await CreateAccountAsync(request.Email, request.Password, isAdmin: first, emailVerified: false, ct);
        var (device, key) = AddDevice(account.Id, deviceName);
        await db.SaveChangesAsync(ct);
        await SendVerificationAsync(account, ct);
        return new DeviceKeyResponse(account.Id, device.Id, key, account.EmailVerified);
    }

    public async Task<DeviceKeyResponse> LoginAsync(LoginRequest request, CancellationToken ct)
    {
        var deviceName = CheckDeviceName(request.DeviceName);
        var normalized = NormalizeEmail(request.Email);
        var account = normalized is null ? null : await db.Accounts.AsNoTracking().SingleOrDefaultAsync(a => a.NormalizedEmail == normalized, ct);
        if (account is null || !VerifyPassword(account, request.Password))
            throw new ApiException(StatusCodes.Status401Unauthorized, ErrorCodes.InvalidCredentials, "Wrong email or password.");

        var (device, key) = AddDevice(account.Id, deviceName);
        await db.SaveChangesAsync(ct);
        events.DevicesChanged(account.Id);
        return new DeviceKeyResponse(account.Id, device.Id, key, account.EmailVerified);
    }

    public async Task ChangePasswordAsync(Guid accountId, Guid deviceId, ChangePasswordRequest request, CancellationToken ct)
    {
        var account = await db.Accounts.AsNoTracking().SingleAsync(a => a.Id == accountId, ct);
        if (!VerifyPassword(account, request.CurrentPassword))
            throw new ApiException(StatusCodes.Status401Unauthorized, ErrorCodes.InvalidCredentials, "The current password is wrong.");

        await SetPasswordAsync(accountId, request.NewPassword, ct);
        if (request.SignOutOtherDevices)
        {
            var ids = await db.Devices.Where(d => d.AccountId == accountId && d.Id != deviceId).Select(d => d.Id).ToListAsync(ct);
            // Exactly the devices listed, so each one hears device-removed (a device added meanwhile stays signed in).
            await db.Devices.Where(d => ids.Contains(d.Id)).ExecuteDeleteAsync(ct);
            events.DevicesRemoved(accountId, ids);
        }
    }

    /// <summary>Removes the account, its devices, Bank and blobs. Devices keep their local Banks.</summary>
    public async Task DeleteAccountAsync(Guid accountId, string? password, CancellationToken ct)
    {
        var account = await db.Accounts.AsNoTracking().SingleAsync(a => a.Id == accountId, ct);
        if (!VerifyPassword(account, password))
            throw new ApiException(StatusCodes.Status401Unauthorized, ErrorCodes.InvalidCredentials, "The password is wrong.");
        await RemoveAccountAsync(accountId, ct);
    }

    /// <summary>Removes the account and everything stored for it, without asking for its password (self-delete and admins).</summary>
    public async Task RemoveAccountAsync(Guid accountId, CancellationToken ct)
    {
        using var _ = await locks.AcquireAsync(accountId, ct);
        await using (var transaction = await db.Database.BeginTransactionAsync(ct))
        {
            await db.Items.Where(i => i.AccountId == accountId).ExecuteDeleteAsync(ct);
            await db.Blobs.Where(b => b.AccountId == accountId).ExecuteDeleteAsync(ct);
            await db.GroupReceipts.Where(r => r.AccountId == accountId).ExecuteDeleteAsync(ct);
            await db.Devices.Where(d => d.AccountId == accountId).ExecuteDeleteAsync(ct);
            await db.PairingCodes.Where(p => p.AccountId == accountId).ExecuteDeleteAsync(ct);
            await db.EmailTokens.Where(t => t.AccountId == accountId).ExecuteDeleteAsync(ct);
            await db.Preferences.Where(p => p.AccountId == accountId).ExecuteDeleteAsync(ct);
            await db.Accounts.Where(a => a.Id == accountId).ExecuteDeleteAsync(ct);
            await transaction.CommitAsync(ct);
        }
        events.AccountRemoved(accountId);
        await blobs.DeleteAllAsync(accountId, ct);
    }

    /// <summary>Validates and stores a new account (no device). Used by sign-up and the admin CLI.</summary>
    public async Task<Account> CreateAccountAsync(string? email, string? password, bool isAdmin, bool emailVerified, CancellationToken ct)
    {
        var (address, normalized) = ParseEmail(email);
        CheckPassword(password);
        if (await db.Accounts.AnyAsync(a => a.NormalizedEmail == normalized, ct))
            throw new ApiException(StatusCodes.Status409Conflict, ErrorCodes.EmailTaken, "An account with this email already exists.");

        var account = new Account
        {
            Id = Guid.NewGuid(), Email = address, NormalizedEmail = normalized,
            IsAdmin = isAdmin, EmailVerified = emailVerified, CreatedUtc = Now,
        };
        account.PasswordHash = Hasher.HashPassword(account, password!);
        db.Accounts.Add(account);
        await db.SaveChangesAsync(ct);
        return account;
    }

    /// <summary>Stores a new password. Written directly so it never races the Bank version token.</summary>
    public async Task SetPasswordAsync(Guid accountId, string? password, CancellationToken ct)
    {
        CheckPassword(password);
        var hash = Hasher.HashPassword(null!, password!);
        await db.Accounts.Where(a => a.Id == accountId).ExecuteUpdateAsync(s => s.SetProperty(a => a.PasswordHash, hash), ct);
    }

    /// <summary>Adds a device with a fresh key; the caller saves. Returns the only copy of the key.</summary>
    public (Device Device, string Key) AddDevice(Guid accountId, string name)
    {
        var key = Secrets.NewKey();
        var device = new Device { Id = Guid.NewGuid(), AccountId = accountId, Name = name, KeyHash = Secrets.Hash(key), CreatedUtc = Now };
        db.Devices.Add(device);
        return (device, key);
    }

    public static bool VerifyPassword(Account account, string? password) =>
        !string.IsNullOrEmpty(password)
        && Hasher.VerifyHashedPassword(account, account.PasswordHash, password) != PasswordVerificationResult.Failed;

    public static string? NormalizeEmail(string? email) =>
        string.IsNullOrWhiteSpace(email) ? null : email.Trim().ToLowerInvariant();

    public static string CheckDeviceName(string? name)
    {
        var trimmed = name?.Trim();
        if (string.IsNullOrEmpty(trimmed) || trimmed.Length > 100)
            throw ApiException.BadRequest(ErrorCodes.InvalidRequest, "Device names are 1 to 100 characters.");
        return trimmed;
    }

    public static readonly TimeSpan VerifyLinkLifetime = TimeSpan.FromDays(7);
    public static readonly TimeSpan ResetLinkLifetime = TimeSpan.FromHours(1);

    public async Task ResendVerificationAsync(Guid accountId, CancellationToken ct) =>
        await SendVerificationAsync(await db.Accounts.AsNoTracking().SingleAsync(a => a.Id == accountId, ct), ct);

    public async Task<bool> VerifyEmailAsync(string? token, CancellationToken ct)
    {
        var row = await TakeTokenAsync(token, EmailTokenPurpose.VerifyEmail, ct);
        if (row is null) return false;
        await db.Accounts.Where(a => a.Id == row.AccountId).ExecuteUpdateAsync(s => s.SetProperty(a => a.EmailVerified, true), ct);
        return true;
    }

    /// <summary>Sends a reset link when the account exists and email works; says nothing either way.</summary>
    public async Task RequestPasswordResetAsync(string? address, CancellationToken ct)
    {
        var normalized = NormalizeEmail(address);
        if (!email.IsEnabled || normalized is null) return;
        var account = await db.Accounts.AsNoTracking().SingleOrDefaultAsync(a => a.NormalizedEmail == normalized, ct);
        if (account is null) return;
        var token = await IssueTokenAsync(account.Id, EmailTokenPurpose.ResetPassword, ResetLinkLifetime, ct);
        await TrySendAsync(account.Email, "Reset your PKForge Sync password", EmailTemplates.Reset(Link("reset-password", token)), ct);
    }

    /// <summary>Sets the new password if the link is valid. Devices stay signed in; the email counts as verified.</summary>
    public async Task<bool> ConfirmPasswordResetAsync(string? token, string? newPassword, CancellationToken ct)
    {
        CheckPassword(newPassword);
        var row = await TakeTokenAsync(token, EmailTokenPurpose.ResetPassword, ct);
        if (row is null) return false;
        await SetPasswordAsync(row.AccountId, newPassword, ct);
        await db.Accounts.Where(a => a.Id == row.AccountId).ExecuteUpdateAsync(s => s.SetProperty(a => a.EmailVerified, true), ct);
        return true;
    }

    private async Task SendVerificationAsync(Account account, CancellationToken ct)
    {
        if (!email.IsEnabled || account.EmailVerified) return;
        var token = await IssueTokenAsync(account.Id, EmailTokenPurpose.VerifyEmail, VerifyLinkLifetime, ct);
        await TrySendAsync(account.Email, "Confirm your PKForge Sync email", EmailTemplates.Verify(Link("verify-email", token)), ct);
    }

    private async Task<string> IssueTokenAsync(Guid accountId, EmailTokenPurpose purpose, TimeSpan lifetime, CancellationToken ct)
    {
        var token = Secrets.NewKey();
        db.EmailTokens.Add(new EmailToken { TokenHash = Secrets.Hash(token), AccountId = accountId, Purpose = purpose, ExpiresUtc = Now + lifetime });
        await db.SaveChangesAsync(ct);
        return token;
    }

    /// <summary>Marks a valid token used and returns it; null for unknown, used or expired tokens.</summary>
    private async Task<EmailToken?> TakeTokenAsync(string? token, EmailTokenPurpose purpose, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(token)) return null;
        var hash = Secrets.Hash(token.Trim());
        var row = await db.EmailTokens.SingleOrDefaultAsync(t => t.TokenHash == hash && t.Purpose == purpose, ct);
        if (row is null || row.Used || row.ExpiresUtc < Now) return null;
        row.Used = true;
        await db.SaveChangesAsync(ct);
        return row;
    }

    private string Link(string page, string token) =>
        $"{options.Value.PublicBaseUrl.TrimEnd('/')}/{page}?token={Uri.EscapeDataString(token)}";

    private async Task TrySendAsync(string to, string subject, string body, CancellationToken ct)
    {
        try { await email.SendAsync(to, subject, body, ct); }
        catch (Exception e) when (e is not OperationCanceledException) { log.LogError(e, "Sending email to {To} failed.", to); }
    }

    private static (string Address, string Normalized) ParseEmail(string? email)
    {
        var address = email?.Trim();
        if (string.IsNullOrEmpty(address) || address.Length > 254 || !MailAddress.TryCreate(address, out var parsed) || parsed.Address != address)
            throw ApiException.BadRequest(ErrorCodes.InvalidRequest, "Enter a valid email address.");
        return (address, address.ToLowerInvariant());
    }

    private static void CheckPassword(string? password)
    {
        if (password is null || password.Length < MinPasswordLength || password.Length > 256)
            throw ApiException.BadRequest(ErrorCodes.WeakPassword, $"Passwords need at least {MinPasswordLength} characters.");
    }
}
