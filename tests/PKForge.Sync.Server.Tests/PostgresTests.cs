using Npgsql;
using PKForge.Sync.Server.Tests.Infrastructure;
using Testcontainers.PostgreSql;

namespace PKForge.Sync.Server.Tests;

public sealed class PostgresFixture : IAsyncLifetime
{
    public PostgreSqlContainer Container { get; } = new PostgreSqlBuilder("postgres:17-alpine").Build();

    public Task InitializeAsync() => Container.StartAsync();
    public Task DisposeAsync() => Container.DisposeAsync().AsTask();

    /// <summary>A fresh database per test; migrations create it.</summary>
    public string NewDatabase() =>
        new NpgsqlConnectionStringBuilder(Container.GetConnectionString()) { Database = $"sync_{Guid.NewGuid():N}" }.ConnectionString;
}

[Trait("Category", "Postgres")]
public sealed class PostgresTests(PostgresFixture postgres) : IClassFixture<PostgresFixture>
{
    [Fact]
    public async Task FullRoundTripOnPostgres()
    {
        using var server = new SyncServerFactory();
        server.Settings["Sync:DatabaseProvider"] = "postgres";
        server.Settings["Sync:ConnectionString"] = postgres.NewDatabase();

        await Scenarios.FullRoundTripAsync(server);
    }

    [Fact]
    public async Task FullRoundTripOnSqlite()
    {
        using var server = new SyncServerFactory();
        await Scenarios.FullRoundTripAsync(server);
    }
}
