namespace PKForge.Sync.Protocol;

public static class SyncProtocol
{
    /// <summary>The protocol version this build speaks.</summary>
    public const int Version = 1;
    /// <summary>The oldest protocol version a server still serves.</summary>
    public const int MinSupportedVersion = 1;
    public const string VersionHeader = "X-PKForge-Protocol";
    public const int SlotsPerBox = 30;
}

public static class ItemKinds
{
    public const string Mon = "mon";
    public const string Boxes = "boxes";
    public const string Settings = "settings";
    /// <summary>The id of the single boxes item and the single settings item of a Bank.</summary>
    public const string SingletonId = "bank";
}

public static class ErrorCodes
{
    public const string UnsupportedProtocol = "unsupported_protocol";
    public const string InvalidRequest = "invalid_request";
    public const string InvalidCredentials = "invalid_credentials";
    public const string EmailTaken = "email_taken";
    public const string RegistrationClosed = "registration_closed";
    public const string WeakPassword = "weak_password";
    public const string EmailNotVerified = "email_not_verified";
    public const string InvalidToken = "invalid_token";
    public const string InvalidPairingCode = "invalid_pairing_code";
    public const string BlobHashMismatch = "blob_hash_mismatch";
    public const string BlobTooLarge = "blob_too_large";
    public const string MissingBlob = "missing_blob";
    public const string InvalidItem = "invalid_item";
    public const string SlotTaken = "slot_taken";
    public const string QuotaExceeded = "quota_exceeded";
    public const string ResyncRequired = "resync_required";
    /// <summary>403: the server is not accepting changes for this account; the message says why.</summary>
    public const string WritesDisabled = "writes_disabled";
    public const string Busy = "busy";
    public const string NotFound = "not_found";
    public const string RateLimited = "rate_limited";
}
