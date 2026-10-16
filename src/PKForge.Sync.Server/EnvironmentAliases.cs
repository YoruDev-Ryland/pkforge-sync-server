namespace PKForge.Sync.Server;

/// <summary>Short environment variable names for docker-compose files, mapped onto Sync:* settings.</summary>
public static class EnvironmentAliases
{
    public static readonly IReadOnlyDictionary<string, string> Map = new Dictionary<string, string>
    {
        ["PUBLIC_URL"] = "Sync:PublicBaseUrl",
        ["DATA_DIR"] = "Sync:DataDirectory",
        ["WEB_ROOT"] = "Sync:WebRoot",
        ["DATABASE_PROVIDER"] = "Sync:DatabaseProvider",
        ["DATABASE_CONNECTION"] = "Sync:ConnectionString",
        ["SERVER_NAME"] = "Sync:ServerName",
        ["ALLOW_REGISTRATION"] = "Sync:AllowRegistration",
        ["REQUIRE_EMAIL_VERIFICATION"] = "Sync:RequireEmailVerification",
        ["TRUST_FORWARDED_HEADERS"] = "Sync:TrustForwardedHeaders",
        ["RELEASED_RETENTION_DAYS"] = "Sync:ReleasedRetentionDays",
        ["SMTP_HOST"] = "Sync:Smtp:Host",
        ["SMTP_PORT"] = "Sync:Smtp:Port",
        ["SMTP_USERNAME"] = "Sync:Smtp:Username",
        ["SMTP_PASSWORD"] = "Sync:Smtp:Password",
        ["SMTP_FROM"] = "Sync:Smtp:From",
    };

    public static IEnumerable<KeyValuePair<string, string?>> Read(Func<string, string?>? environment = null)
    {
        environment ??= Environment.GetEnvironmentVariable;
        foreach (var (name, key) in Map)
        {
            if (environment(name) is { Length: > 0 } value) yield return new(key, value);
        }
    }
}
