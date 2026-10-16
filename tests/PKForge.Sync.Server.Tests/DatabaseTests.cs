using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PKForge.Sync.Server.Data;
using PKForge.Sync.Server.Tests.Infrastructure;

namespace PKForge.Sync.Server.Tests;

public sealed class DatabaseTests
{
    [Fact]
    public async Task StartupCreatesTheSqliteDatabase()
    {
        using var server = new SyncServerFactory();
        (await server.Client().GetAsync("/v1/server-info")).EnsureSuccessStatusCode();

        Assert.True(File.Exists(Path.Combine(server.DataDirectory, "sync.db")));
        using var scope = server.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SyncDbContext>();
        Assert.Equal(0, await db.Accounts.CountAsync());
        Assert.Empty(await db.Database.GetPendingMigrationsAsync());
    }

    [Fact]
    public void BothProvidersHaveMigrationsForTheCurrentModel()
    {
        using var sqlite = new SqliteDesignTimeFactory().CreateDbContext([]);
        using var postgres = new PostgresDesignTimeFactory().CreateDbContext([]);

        Assert.False(sqlite.Database.HasPendingModelChanges());
        Assert.False(postgres.Database.HasPendingModelChanges());
    }
}
