using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace PKForge.Sync.Server.Data;

public static class DataSetup
{
    /// <summary>Registers both contexts lazily; <see cref="SyncDbContext"/> resolves to the configured provider.</summary>
    public static IServiceCollection AddSyncData(this IServiceCollection services)
    {
        services.AddDbContext<SqliteSyncDbContext>((sp, o) => o.UseSqlite(SqliteConnectionString(Options(sp))));
        services.AddDbContext<PostgresSyncDbContext>((sp, o) => o.UseNpgsql(Options(sp).ConnectionString
            ?? throw new InvalidOperationException("Sync:ConnectionString (DATABASE_CONNECTION) is required for postgres.")));
        services.AddScoped<SyncDbContext>(sp => Options(sp).UsesPostgres
            ? sp.GetRequiredService<PostgresSyncDbContext>()
            : sp.GetRequiredService<SqliteSyncDbContext>());
        return services;
    }

    /// <summary>Creates the data directory and applies pending migrations.</summary>
    public static void MigrateDatabase(this IServiceProvider services)
    {
        var options = Options(services);
        Directory.CreateDirectory(options.DataDirectory);
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SyncDbContext>();
        db.Database.Migrate();
        if (db.Database.IsSqlite()) db.Database.ExecuteSqlRaw("PRAGMA journal_mode=WAL;");
    }

    private static SyncOptions Options(IServiceProvider sp) => sp.GetRequiredService<IOptions<SyncOptions>>().Value;

    private static string SqliteConnectionString(SyncOptions options) =>
        options.ConnectionString ?? $"Data Source={Path.Combine(options.DataDirectory, "sync.db")}";
}
