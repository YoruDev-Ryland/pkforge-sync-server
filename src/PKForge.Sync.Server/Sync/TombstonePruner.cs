using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using PKForge.Sync.Server.Blobs;
using PKForge.Sync.Server.Data;

namespace PKForge.Sync.Server.Sync;

/// <summary>Removes old tombstones, blobs no Pokémon uses, and old push receipts.</summary>
public sealed class TombstonePruner(SyncDbContext db, IBlobStore store, AccountLocks locks, IOptions<SyncOptions> options, TimeProvider time)
{
    /// <summary>A fresh upload waits this long for the push that uses it.</summary>
    public static readonly TimeSpan BlobGrace = TimeSpan.FromDays(1);
    public static readonly TimeSpan ReceiptRetention = TimeSpan.FromDays(30);

    public async Task PruneAsync(CancellationToken ct)
    {
        var now = time.GetUtcNow().UtcDateTime;
        await ForgetReleasesAsync(now.AddDays(-options.Value.ReleasedRetentionDays), ct);
        await PruneTombstonesAsync(now.AddDays(-options.Value.TombstoneRetentionDays), ct);
        await PruneBlobsAsync(now - BlobGrace, ct);
        await db.GroupReceipts.Where(r => r.CreatedUtc < now - ReceiptRetention).ExecuteDeleteAsync(ct);
    }

    /// <summary>Releases older than the window become final: their tombstones drop the kept value and blob,
    /// so the blob sweep below removes bytes nothing else uses.</summary>
    private async Task ForgetReleasesAsync(DateTime cutoff, CancellationToken ct)
    {
        var accounts = await db.Items.Where(i => i.Deleted && (i.Blob != null || i.ReleasedValue != null) && i.UpdatedUtc <= cutoff).Select(i => i.AccountId).Distinct().ToListAsync(ct);
        foreach (var accountId in accounts)
        {
            using var _ = await locks.AcquireAsync(accountId, ct);
            await db.Items.Where(i => i.AccountId == accountId && i.Deleted && (i.Blob != null || i.ReleasedValue != null) && i.UpdatedUtc <= cutoff)
                .ExecuteUpdateAsync(s => s.SetProperty(i => i.Blob, (string?)null).SetProperty(i => i.ReleasedValue, (string?)null), ct);
        }
    }

    private async Task PruneTombstonesAsync(DateTime cutoff, CancellationToken ct)
    {
        var accounts = await db.Items.Where(i => i.Deleted && i.UpdatedUtc < cutoff).Select(i => i.AccountId).Distinct().ToListAsync(ct);
        foreach (var accountId in accounts)
        {
            using var _ = await locks.AcquireAsync(accountId, ct);
            await using var transaction = await db.Database.BeginTransactionAsync(ct);
            var stale = db.Items.Where(i => i.AccountId == accountId && i.Deleted && i.UpdatedUtc < cutoff);
            if (await stale.MaxAsync(i => (long?)i.Version, ct) is not { } through) continue;
            await stale.ExecuteDeleteAsync(ct);
            await db.Accounts.Where(a => a.Id == accountId && a.PrunedThroughVersion < through)
                .ExecuteUpdateAsync(s => s.SetProperty(a => a.PrunedThroughVersion, through), ct);
            await transaction.CommitAsync(ct);
        }
    }

    private async Task PruneBlobsAsync(DateTime cutoff, CancellationToken ct)
    {
        while (true)
        {
            var orphans = await db.Blobs.AsNoTracking()
                .Where(b => b.CreatedUtc < cutoff && !db.Items.Any(i => i.AccountId == b.AccountId && i.Blob == b.Hash))
                .OrderBy(b => b.AccountId).Take(500).ToListAsync(ct);
            if (orphans.Count == 0) return;

            foreach (var account in orphans.GroupBy(b => b.AccountId))
            {
                using var _ = await locks.AcquireAsync(account.Key, ct);
                foreach (var blob in account)
                {
                    // Checked again under the lock: a push may have started using it.
                    var removed = await db.Blobs
                        .Where(b => b.AccountId == blob.AccountId && b.Hash == blob.Hash && b.CreatedUtc < cutoff
                            && !db.Items.Any(i => i.AccountId == b.AccountId && i.Blob == b.Hash))
                        .ExecuteDeleteAsync(ct);
                    if (removed > 0) await store.DeleteAsync(blob.AccountId, blob.Hash, ct);
                }
            }
        }
    }
}

public sealed class PruneBackgroundService(IServiceScopeFactory scopes, IOptions<SyncOptions> options, ILogger<PruneBackgroundService> log)
    : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.Value.EnablePruning) return;
        using var timer = new PeriodicTimer(TimeSpan.FromHours(6));
        do
        {
            try
            {
                using var scope = scopes.CreateScope();
                await scope.ServiceProvider.GetRequiredService<TombstonePruner>().PruneAsync(stoppingToken);
            }
            catch (Exception e) when (e is not OperationCanceledException)
            {
                log.LogError(e, "Pruning failed; retrying at the next interval.");
            }
        } while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}
