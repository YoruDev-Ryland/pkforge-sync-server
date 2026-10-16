namespace PKForge.Sync.Server;

public sealed class SyncOptions
{
    public const string Section = "Sync";

    /// <summary>Holds the SQLite database (unless a connection string is given) and the blob store.</summary>
    public string DataDirectory { get; set; } = "data";
    /// <summary>"sqlite" (default) or "postgres".</summary>
    public string DatabaseProvider { get; set; } = "sqlite";
    /// <summary>Overrides the database connection; required for postgres.</summary>
    public string? ConnectionString { get; set; }
    /// <summary>The address devices and emails use to reach this server.</summary>
    public string PublicBaseUrl { get; set; } = "http://localhost:8080";
    public const string DefaultServerName = "PKForge Sync";
    public const int MaxServerNameLength = 60;
    /// <summary>The name apps and the web Bank show for this server (trimmed, at most 60 characters).</summary>
    public string ServerName { get; set; } = DefaultServerName;
    public bool AllowRegistration { get; set; }
    public bool RequireEmailVerification { get; set; }
    public int MaxLiveMonsPerAccount { get; set; } = 100_000;
    public int MaxBlobBytes { get; set; } = 64 * 1024;
    public int AuthRequestsPerMinute { get; set; } = 10;
    public int TombstoneRetentionDays { get; set; } = 180;
    /// <summary>How long a released Pokémon can be restored from the web Bank (0..TombstoneRetentionDays; 0 = releases are final).</summary>
    public int ReleasedRetentionDays { get; set; } = 30;
    public bool EnablePruning { get; set; } = true;
    /// <summary>How often GET /v1/sync/events sends a keep-alive comment, so proxies don't close an idle stream.</summary>
    public int EventHeartbeatSeconds { get; set; } = 25;
    /// <summary>Honour X-Forwarded-For/Proto; only behind a reverse proxy you control.</summary>
    public bool TrustForwardedHeaders { get; set; }
    public SmtpOptions Smtp { get; set; } = new();
    /// <summary>The built web app (index.html + assets). Relative paths resolve against the app's folder.</summary>
    public string WebRoot { get; set; } = "wwwroot";
    /// <summary>PKHeX sprites, balls and wallpapers copied at build time.</summary>
    public string PkhexAssets { get; set; } = "pkhex";

    /// <summary><see cref="ServerName"/> as shown: trimmed, the default when blank, cut to 60 characters.</summary>
    public string DisplayName => ServerName?.Trim() is { Length: > 0 } name
        ? (name.Length > MaxServerNameLength ? name[..MaxServerNameLength].TrimEnd() : name)
        : DefaultServerName;

    public bool UsesPostgres => string.Equals(DatabaseProvider, "postgres", StringComparison.OrdinalIgnoreCase);
}

public sealed class SmtpOptions
{
    public string? Host { get; set; }
    public int Port { get; set; } = 587;
    public string? Username { get; set; }
    public string? Password { get; set; }
    public string From { get; set; } = "PKForge Sync <no-reply@localhost>";

    public bool Enabled => !string.IsNullOrWhiteSpace(Host);
}
