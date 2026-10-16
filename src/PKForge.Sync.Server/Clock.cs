namespace PKForge.Sync.Server;

/// <summary>Entities store UTC <see cref="DateTime"/> (SQLite cannot compare DateTimeOffset); the protocol uses offsets.</summary>
public static class Clock
{
    public static DateTimeOffset ToOffset(DateTime utc) => new(DateTime.SpecifyKind(utc, DateTimeKind.Utc));
}
