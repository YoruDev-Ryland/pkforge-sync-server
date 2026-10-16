using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace PKForge.Sync.Server.Data;

/// <summary>
/// One model, two providers. Each provider has its own context type so each keeps its own migrations
/// (Data/Migrations/Sqlite and Data/Migrations/Postgres).
/// </summary>
public abstract class SyncDbContext(DbContextOptions options) : DbContext(options)
{
    public DbSet<Account> Accounts => Set<Account>();
    public DbSet<Device> Devices => Set<Device>();
    public DbSet<PairingCode> PairingCodes => Set<PairingCode>();
    public DbSet<EmailToken> EmailTokens => Set<EmailToken>();
    public DbSet<Item> Items => Set<Item>();
    public DbSet<Blob> Blobs => Set<Blob>();
    public DbSet<GroupReceipt> GroupReceipts => Set<GroupReceipt>();
    public DbSet<AccountPreferences> Preferences => Set<AccountPreferences>();

    protected override void OnModelCreating(ModelBuilder model)
    {
        model.Entity<Account>(e =>
        {
            e.HasKey(a => a.Id);
            e.Property(a => a.Email).HasMaxLength(254);
            e.Property(a => a.NormalizedEmail).HasMaxLength(254);
            e.Property(a => a.Epoch).HasMaxLength(32);
            e.HasIndex(a => a.NormalizedEmail).IsUnique();
            // Two server instances pushing to one account at once: the second save fails instead of
            // handing out the same versions twice.
            e.Property(a => a.Version).IsConcurrencyToken();
        });
        model.Entity<Device>(e =>
        {
            e.HasKey(d => d.Id);
            e.Property(d => d.Name).HasMaxLength(100);
            e.Property(d => d.KeyHash).HasMaxLength(64);
            e.HasIndex(d => d.KeyHash).IsUnique();
            e.HasIndex(d => d.AccountId);
            e.HasOne<Account>().WithMany().HasForeignKey(d => d.AccountId).OnDelete(DeleteBehavior.Cascade);
        });
        model.Entity<PairingCode>(e =>
        {
            e.HasKey(p => p.CodeHash);
            e.Property(p => p.CodeHash).HasMaxLength(64);
            e.HasIndex(p => p.AccountId);
            e.HasOne<Account>().WithMany().HasForeignKey(p => p.AccountId).OnDelete(DeleteBehavior.Cascade);
        });
        model.Entity<EmailToken>(e =>
        {
            e.HasKey(t => t.TokenHash);
            e.Property(t => t.TokenHash).HasMaxLength(64);
            e.HasIndex(t => t.AccountId);
            e.HasOne<Account>().WithMany().HasForeignKey(t => t.AccountId).OnDelete(DeleteBehavior.Cascade);
        });
        model.Entity<Item>(e =>
        {
            e.HasKey(i => new { i.AccountId, i.Kind, i.Id });
            e.Property(i => i.Kind).HasMaxLength(16);
            e.Property(i => i.Id).HasMaxLength(64);
            e.Property(i => i.Blob).HasMaxLength(64);
            e.HasIndex(i => new { i.AccountId, i.Version }).IsUnique();
            // Slot uniqueness is enforced by SyncService under the account lock, not by a unique index:
            // a box remap moves many Pokémon in one save, and row-by-row updates would collide midway.
            e.HasIndex(i => new { i.AccountId, i.Kind, i.Box });
            e.HasIndex(i => new { i.AccountId, i.Blob });
            e.HasIndex(i => new { i.Deleted, i.UpdatedUtc });
            e.HasOne<Account>().WithMany().HasForeignKey(i => i.AccountId).OnDelete(DeleteBehavior.Cascade);
        });
        model.Entity<Blob>(e =>
        {
            e.HasKey(b => new { b.AccountId, b.Hash });
            e.Property(b => b.Hash).HasMaxLength(64);
            e.HasIndex(b => b.CreatedUtc);
            e.HasOne<Account>().WithMany().HasForeignKey(b => b.AccountId).OnDelete(DeleteBehavior.Cascade);
        });
        model.Entity<GroupReceipt>(e =>
        {
            e.HasKey(r => new { r.AccountId, r.ClientGroupId });
            e.HasIndex(r => r.CreatedUtc);
            e.HasOne<Account>().WithMany().HasForeignKey(r => r.AccountId).OnDelete(DeleteBehavior.Cascade);
        });
        model.Entity<AccountPreferences>(e =>
        {
            e.HasKey(p => p.AccountId);
            e.Property(p => p.Json).HasMaxLength(4096);
            e.HasOne<Account>().WithMany().HasForeignKey(p => p.AccountId).OnDelete(DeleteBehavior.Cascade);
        });
    }
}

public sealed class SqliteSyncDbContext(DbContextOptions<SqliteSyncDbContext> options) : SyncDbContext(options);

public sealed class PostgresSyncDbContext(DbContextOptions<PostgresSyncDbContext> options) : SyncDbContext(options);

/// <summary>Used by `dotnet ef` and tests; never opens a connection.</summary>
public sealed class SqliteDesignTimeFactory : IDesignTimeDbContextFactory<SqliteSyncDbContext>
{
    public SqliteSyncDbContext CreateDbContext(string[] args) =>
        new(new DbContextOptionsBuilder<SqliteSyncDbContext>().UseSqlite("Data Source=design.db").Options);
}

public sealed class PostgresDesignTimeFactory : IDesignTimeDbContextFactory<PostgresSyncDbContext>
{
    public PostgresSyncDbContext CreateDbContext(string[] args) =>
        new(new DbContextOptionsBuilder<PostgresSyncDbContext>().UseNpgsql("Host=localhost;Database=design").Options);
}
