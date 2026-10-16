using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using PKForge.Sync.Protocol;
using PKForge.Sync.Server.Data;
using PKForge.Sync.Server.Http;
using PKForge.Sync.Server.Sync;

namespace PKForge.Sync.Server.Blobs;

public sealed class BlobService(SyncDbContext db, IBlobStore store, AccountLocks locks, IOptions<SyncOptions> options, TimeProvider time)
{
    public const int MaxHashesPerQuery = 1000;
    public const int MaxBlobsPerBatch = 500;

    public async Task<MissingBlobsResponse> MissingAsync(Guid accountId, MissingBlobsRequest request, CancellationToken ct)
    {
        var hashes = request.Hashes ?? [];
        if (hashes.Count > MaxHashesPerQuery || !hashes.All(BlobHash.IsValid))
            throw ApiException.BadRequest(ErrorCodes.InvalidRequest, $"Ask about at most {MaxHashesPerQuery} lowercase SHA-256 blob ids.");

        var distinct = hashes.Distinct().ToList();
        var known = await db.Blobs.Where(b => b.AccountId == accountId && distinct.Contains(b.Hash)).Select(b => b.Hash).ToListAsync(ct);
        var stored = new List<string>(known.Count);
        foreach (var hash in known)
        {
            // A record whose file is gone counts as missing, so the device uploads it again.
            if (await store.ExistsAsync(accountId, hash, ct)) stored.Add(hash);
        }
        return new MissingBlobsResponse(distinct.Except(stored).ToList());
    }

    public async Task PutAsync(Guid accountId, string hash, Stream body, CancellationToken ct)
    {
        if (!BlobHash.IsValid(hash)) throw ApiException.BadRequest(ErrorCodes.InvalidRequest, "Blob ids are lowercase SHA-256 hex.");
        var data = await ReadLimitedAsync(body, options.Value.MaxBlobBytes, ct);
        Check(hash, data);
        await StoreAsync(accountId, hash, data, ct);
    }

    /// <summary>Stores a batch; every blob is checked before any is stored, so a bad one rejects the batch.</summary>
    public async Task UploadManyAsync(Guid accountId, UploadBlobsRequest request, CancellationToken ct)
    {
        var blobs = request.Blobs ?? [];
        if (blobs.Count > MaxBlobsPerBatch)
            throw ApiException.BadRequest(ErrorCodes.InvalidRequest, $"Upload at most {MaxBlobsPerBatch} blobs at once.");
        foreach (var blob in blobs) Check(blob?.Hash, blob?.Data);
        foreach (var blob in blobs) await StoreAsync(accountId, blob.Hash, blob.Data, ct);
    }

    /// <summary>The requested blobs this account has; unknown ids are left out.</summary>
    public async Task<DownloadBlobsResponse> DownloadManyAsync(Guid accountId, DownloadBlobsRequest request, CancellationToken ct)
    {
        var hashes = request.Hashes ?? [];
        if (hashes.Count > MaxBlobsPerBatch || !hashes.All(BlobHash.IsValid))
            throw ApiException.BadRequest(ErrorCodes.InvalidRequest, $"Ask for at most {MaxBlobsPerBatch} lowercase SHA-256 blob ids.");
        var distinct = hashes.Distinct().ToList();
        var known = await db.Blobs.Where(b => b.AccountId == accountId && distinct.Contains(b.Hash)).Select(b => b.Hash).ToListAsync(ct);
        var blobs = new List<BlobData>(known.Count);
        foreach (var hash in known)
        {
            if (await store.GetAsync(accountId, hash, ct) is { } data) blobs.Add(new BlobData(hash, data));
        }
        return new DownloadBlobsResponse(blobs);
    }

    private void Check(string? hash, byte[]? data)
    {
        if (!BlobHash.IsValid(hash)) throw ApiException.BadRequest(ErrorCodes.InvalidRequest, "Blob ids are lowercase SHA-256 hex.");
        if (data is null) throw ApiException.BadRequest(ErrorCodes.InvalidRequest, "A blob needs its bytes.");
        if (data.Length > options.Value.MaxBlobBytes)
            throw new ApiException(StatusCodes.Status413PayloadTooLarge, ErrorCodes.BlobTooLarge, $"Blobs are at most {options.Value.MaxBlobBytes} bytes.");
        if (BlobHash.Compute(data) != hash) throw ApiException.BadRequest(ErrorCodes.BlobHashMismatch, "The bytes don't match the blob id.");
    }

    private async Task StoreAsync(Guid accountId, string hash, byte[] data, CancellationToken ct)
    {
        var now = time.GetUtcNow().UtcDateTime;
        // Under the account lock the orphan sweep cannot delete this blob between our write and our record.
        using var _ = await locks.AcquireAsync(accountId, ct);
        // Already recorded: refresh its age so the sweep leaves a blob that is about to be used alone,
        // and restore the file if it went missing.
        if (await db.Blobs.Where(b => b.AccountId == accountId && b.Hash == hash).ExecuteUpdateAsync(s => s.SetProperty(b => b.CreatedUtc, now), ct) > 0)
        {
            if (!await store.ExistsAsync(accountId, hash, ct)) await store.PutAsync(accountId, hash, data, ct);
            return;
        }

        await store.PutAsync(accountId, hash, data, ct);
        db.Blobs.Add(new Blob { AccountId = accountId, Hash = hash, Size = data.Length, CreatedUtc = now });
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            // Another upload of the same bytes won the race; that is the same outcome.
            if (!await db.Blobs.AsNoTracking().AnyAsync(b => b.AccountId == accountId && b.Hash == hash, ct)) throw;
        }
    }

    public async Task<byte[]?> GetAsync(Guid accountId, string hash, CancellationToken ct) =>
        BlobHash.IsValid(hash) && await db.Blobs.AnyAsync(b => b.AccountId == accountId && b.Hash == hash, ct)
            ? await store.GetAsync(accountId, hash, ct)
            : null;

    private static async Task<byte[]> ReadLimitedAsync(Stream body, int max, CancellationToken ct)
    {
        using var buffer = new MemoryStream();
        var chunk = new byte[8192];
        int read;
        while ((read = await body.ReadAsync(chunk, ct)) > 0)
        {
            buffer.Write(chunk, 0, read);
            if (buffer.Length > max)
                throw new ApiException(StatusCodes.Status413PayloadTooLarge, ErrorCodes.BlobTooLarge, $"Blobs are at most {max} bytes.");
        }
        return buffer.ToArray();
    }
}
