namespace PKForge.Sync.Server.Data;

public sealed class Account
{
    public Guid Id { get; set; }
    public required string Email { get; set; }
    /// <summary>Trimmed, lower-case email: the unique login.</summary>
    public required string NormalizedEmail { get; set; }
    public string PasswordHash { get; set; } = "";
    public bool EmailVerified { get; set; }
    public bool IsAdmin { get; set; }
    public DateTime CreatedUtc { get; set; }
    /// <summary>The Bank's version: the highest version any of this account's items carries.</summary>
    public long Version { get; set; }
    /// <summary>Tombstones up to this version were pruned; a device whose cursor is lower must resync.</summary>
    public long PrunedThroughVersion { get; set; }
    /// <summary>
    /// Names this Bank's history. Rotated when a device turns out to be ahead of the server (a restored
    /// backup), so every device holding a cursor into the lost history is told to resync.
    /// </summary>
    public string Epoch { get; set; } = NewEpoch();

    public static string NewEpoch() => Guid.NewGuid().ToString("N");
}

public sealed class Device
{
    public Guid Id { get; set; }
    public Guid AccountId { get; set; }
    public required string Name { get; set; }
    /// <summary>SHA-256 of the device key; the key itself is never stored.</summary>
    public required string KeyHash { get; set; }
    public DateTime CreatedUtc { get; set; }
    public DateTime? LastSyncUtc { get; set; }
}

public sealed class PairingCode
{
    public required string CodeHash { get; set; }
    public Guid AccountId { get; set; }
    public DateTime ExpiresUtc { get; set; }
    public bool Used { get; set; }
}

public enum EmailTokenPurpose { VerifyEmail = 1, ResetPassword = 2 }

public sealed class EmailToken
{
    public required string TokenHash { get; set; }
    public Guid AccountId { get; set; }
    public EmailTokenPurpose Purpose { get; set; }
    public DateTime ExpiresUtc { get; set; }
    public bool Used { get; set; }
}

/// <summary>One synced item of a Bank. Box, Slot and Blob mirror a live mon's value for queries.</summary>
public sealed class Item
{
    public Guid AccountId { get; set; }
    public required string Kind { get; set; }
    public required string Id { get; set; }
    public long Version { get; set; }
    public bool Deleted { get; set; }
    /// <summary>The value's JSON; null for tombstones.</summary>
    public string? Value { get; set; }
    public int? Box { get; set; }
    public int? Slot { get; set; }
    /// <summary>
    /// A live mon's blob. A released mon's tombstone keeps it (with <see cref="ReleasedValue"/>) while the
    /// release can still be undone, so the bytes are not pruned; devices never see either.
    /// </summary>
    public string? Blob { get; set; }
    /// <summary>A released mon's last live value JSON, kept on its tombstone for "Recently released".</summary>
    public string? ReleasedValue { get; set; }
    public DateTime UpdatedUtc { get; set; }
}

public sealed class Blob
{
    public Guid AccountId { get; set; }
    public required string Hash { get; set; }
    public long Size { get; set; }
    public DateTime CreatedUtc { get; set; }
}

/// <summary>The stored result of an accepted change group, so a retried push is answered, not re-applied.</summary>
public sealed class GroupReceipt
{
    public Guid AccountId { get; set; }
    public Guid ClientGroupId { get; set; }
    public required string ResultJson { get; set; }
    public DateTime CreatedUtc { get; set; }
}

/// <summary>Web-only settings of an account (theme), a small JSON object the server does not interpret.</summary>
public sealed class AccountPreferences
{
    public Guid AccountId { get; set; }
    public required string Json { get; set; }
    public DateTime UpdatedUtc { get; set; }
}

/// <summary>A server-wide setting an admin changed from the web; its absence means the configured value applies.</summary>
public sealed class ServerSetting
{
    public required string Key { get; set; }
    public required string Value { get; set; }
    public DateTime UpdatedUtc { get; set; }
}
