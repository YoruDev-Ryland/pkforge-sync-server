using System.Text.Json;

namespace PKForge.Sync.Protocol;

public sealed record ErrorResponse(string Code, string Message);

/// <summary><see cref="Name"/> is the operator's name for this server, shown to users.</summary>
public sealed record ServerInfo(
    int MinProtocol, int MaxProtocol, string Name,
    bool RegistrationOpen, bool EmailEnabled, bool EmailVerificationRequired);

public sealed record RegisterRequest(string Email, string Password, string DeviceName);
public sealed record LoginRequest(string Email, string Password, string DeviceName);
public sealed record DeviceKeyResponse(Guid AccountId, Guid DeviceId, string DeviceKey, bool EmailVerified);
public sealed record VerifyEmailRequest(string Token);
public sealed record PasswordResetRequest(string Email);
public sealed record PasswordResetConfirmRequest(string Token, string NewPassword);
public sealed record ChangePasswordRequest(string CurrentPassword, string NewPassword, bool SignOutOtherDevices);
public sealed record DeleteAccountRequest(string Password);

public sealed record DeviceInfo(Guid Id, string Name, DateTimeOffset CreatedUtc, DateTimeOffset? LastSyncUtc, bool IsCurrent);

public sealed record PairingCodeResponse(string Code, string QrPayload, DateTimeOffset ExpiresUtc);
public sealed record RedeemPairingRequest(string Code, string DeviceName);

public sealed record MissingBlobsRequest(IReadOnlyList<string> Hashes);
public sealed record MissingBlobsResponse(IReadOnlyList<string> Missing);

/// <summary>One item write. <see cref="BaseVersion"/> 0 means "new item".</summary>
public sealed record ItemWrite(string Kind, string Id, long BaseVersion, JsonElement? Value, bool Deleted = false);
/// <summary>Writes the server applies all-or-nothing. Retrying the same <see cref="ClientGroupId"/> is safe.</summary>
public sealed record ChangeGroup(Guid ClientGroupId, IReadOnlyList<ItemWrite> Writes);
/// <summary><see cref="Since"/> and <see cref="Epoch"/> are the device's cursor; the server refuses a push from a
/// device whose history it no longer shares (410 resync_required).</summary>
public sealed record PushRequest(IReadOnlyList<ChangeGroup> Groups, long? Since = null, string? Epoch = null);

public enum GroupStatus { Accepted, Conflict, SlotTaken, Rejected }

/// <summary>
/// Accepted: the group's items as stored. Conflict: the server's current copy of each conflicting item
/// (version 0 = the item does not exist). SlotTaken: the Pokémon occupying the requested slots.
/// Rejected: <see cref="Error"/> says why; nothing changed.
/// </summary>
public sealed record GroupResult(Guid ClientGroupId, GroupStatus Status, IReadOnlyList<SyncItem> Items, string? Error = null, string? Message = null);
public sealed record PushResponse(IReadOnlyList<GroupResult> Results, long Version, string Epoch);

public sealed record SyncItem(string Kind, string Id, long Version, bool Deleted, JsonElement? Value);
/// <summary><see cref="Epoch"/> names the Bank's history; a device keeps it with its cursor and sends both back.</summary>
public sealed record ChangesResponse(IReadOnlyList<SyncItem> Items, long Next, bool HasMore, string Epoch);

/// <summary>Pokémon bytes with their id, for batch upload and download.</summary>
public sealed record BlobData(string Hash, byte[] Data);
public sealed record UploadBlobsRequest(IReadOnlyList<BlobData> Blobs);
public sealed record DownloadBlobsRequest(IReadOnlyList<string> Hashes);
public sealed record DownloadBlobsResponse(IReadOnlyList<BlobData> Blobs);

/// <summary>Web sign-in: email + password, or a pairing code from the app.</summary>
public sealed record WebSessionRequest(string? Email, string? Password, string? PairingCode, string DeviceName);
public sealed record WebSessionResponse(Guid AccountId, Guid DeviceId, bool EmailVerified);

/// <summary>
/// The web Bank's "Recently released": released Pokémon that can still be restored, newest first.
/// <see cref="RetentionDays"/> 0 means releases are final (the list is always empty).
/// </summary>
public sealed record ReleasedResponse(int RetentionDays, IReadOnlyList<ReleasedMon> Items);
/// <summary>A released Pokémon: its tombstone's id and version (the base version to restore it with), when it was
/// released, and its last value (a <see cref="MonValue"/>; its blob is kept until the window ends).</summary>
public sealed record ReleasedMon(string Id, long Version, DateTimeOffset ReleasedUtc, JsonElement Value);
