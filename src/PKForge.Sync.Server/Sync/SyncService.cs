using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using PKForge.Sync.Protocol;
using PKForge.Sync.Server.Accounts;
using PKForge.Sync.Server.Data;
using PKForge.Sync.Server.Http;
using PKForge.Sync.Server.Web;

namespace PKForge.Sync.Server.Sync;

public sealed class SyncService(
    SyncDbContext db, AccountLocks locks, IAccountWritePolicy writePolicy, IOptions<SyncOptions> options, TimeProvider time,
    AccountEvents events)
{
    public const int MaxGroupsPerPush = 500;
    public const int MaxWritesPerPush = 110_000;
    public const string WritesDisabledMessage = "This server isn't accepting changes for this account.";

    private DateTime Now => time.GetUtcNow().UtcDateTime;

    public async Task<PushResponse> PushAsync(Guid accountId, Guid deviceId, PushRequest request, CancellationToken ct)
    {
        if (request.Groups is null || request.Groups.Count > MaxGroupsPerPush
            || request.Groups.Any(g => g?.Writes is null) || request.Groups.Sum(g => g.Writes.Count) > MaxWritesPerPush)
            throw ApiException.BadRequest(ErrorCodes.InvalidRequest, $"A push carries at most {MaxGroupsPerPush} groups and {MaxWritesPerPush} writes.");

        if (await writePolicy.CheckAsync(accountId, ct) is { Allowed: false } decision)
            throw new ApiException(StatusCodes.Status403Forbidden, ErrorCodes.WritesDisabled,
                string.IsNullOrWhiteSpace(decision.Reason) ? WritesDisabledMessage : decision.Reason);

        using var _ = await locks.AcquireAsync(accountId, ct);
        var account = await db.Accounts.SingleAsync(a => a.Id == accountId, ct);
        var startVersion = account.Version;
        var savedVersion = startVersion;
        try
        {
            return await PushGroupsAsync(account, deviceId, request, v => savedVersion = v, ct);
        }
        finally
        {
            // Each accepted group commits on its own, so even a push that fails part-way may have stored something:
            // the account's streams then fetch what changed.
            if (savedVersion != startVersion) events.BankChanged(accountId, savedVersion);
        }
    }

    private async Task<PushResponse> PushGroupsAsync(Account account, Guid deviceId, PushRequest request, Action<long> saved, CancellationToken ct)
    {
        var accountId = account.Id;
        await CheckCursorAsync(account, request.Since ?? 0, request.Epoch, ct);
        var results = new List<GroupResult>(request.Groups.Count);
        foreach (var group in request.Groups)
        {
            var receipt = await db.GroupReceipts.AsNoTracking()
                .SingleOrDefaultAsync(r => r.AccountId == accountId && r.ClientGroupId == group.ClientGroupId, ct);
            if (receipt is not null)
            {
                results.Add(JsonSerializer.Deserialize<GroupResult>(receipt.ResultJson, ProtocolJson.Options)!);
                continue;
            }

            var result = await ApplyGroupAsync(account, group, ct);
            if (result.Status == GroupStatus.Accepted)
            {
                db.GroupReceipts.Add(new GroupReceipt
                {
                    AccountId = accountId, ClientGroupId = group.ClientGroupId, CreatedUtc = Now,
                    ResultJson = JsonSerializer.Serialize(result, ProtocolJson.Options),
                });
                await SaveAsync(ct);
                saved(account.Version);
            }
            results.Add(result);
        }

        await TouchDeviceAsync(deviceId, ct);
        return new PushResponse(results, account.Version, account.Epoch);
    }

    public const int DefaultChangesPage = 500;
    public const int MaxChangesPage = 1000;

    /// <summary>Items changed after <paramref name="since"/>, oldest first. Since 0 is a full download.</summary>
    public async Task<ChangesResponse> ChangesAsync(Guid accountId, Guid deviceId, long since, string? epoch, int? limit, CancellationToken ct)
    {
        var take = Math.Clamp(limit ?? DefaultChangesPage, 1, MaxChangesPage);

        var account = await db.Accounts.SingleAsync(a => a.Id == accountId, ct);
        await CheckCursorAsync(account, since, epoch, ct);
        if (since > 0 && since < account.PrunedThroughVersion) throw ResyncRequired();

        var rows = await db.Items.AsNoTracking()
            .Where(i => i.AccountId == accountId && i.Version > since)
            .OrderBy(i => i.Version)
            .Take(take + 1)
            .ToListAsync(ct);
        var hasMore = rows.Count > take;
        if (hasMore) rows.RemoveAt(take);

        await TouchDeviceAsync(deviceId, ct);
        return new ChangesResponse(rows.Select(ToSyncItem).ToList(), rows.Count > 0 ? rows[^1].Version : since, hasMore, account.Epoch);
    }

    public static SyncItem ToSyncItem(Item item) => new(item.Kind, item.Id, item.Version, item.Deleted,
        item.Value is null ? null : JsonSerializer.Deserialize<JsonElement>(item.Value));

    /// <summary>
    /// Validates the whole group against current state, then stages its writes on the tracked account and items.
    /// Nothing is staged unless the group is accepted; the caller saves.
    /// </summary>
    private async Task<GroupResult> ApplyGroupAsync(Account account, ChangeGroup group, CancellationToken ct)
    {
        // 1. Shape.
        var writes = new List<ParsedWrite>(group.Writes.Count);
        var keys = new HashSet<(string, string)>();
        foreach (var raw in group.Writes)
        {
            var write = ParsedWrite.TryParse(raw, out var error);
            if (write is null) return Rejected(group, ErrorCodes.InvalidItem, error);
            if (!keys.Add((write.Kind, write.Id))) return Rejected(group, ErrorCodes.InvalidItem, $"{write.Kind} {write.Id} appears twice in one group.");
            writes.Add(write);
        }

        // 2. Current state of everything the group touches (plus the two singletons, needed for box checks).
        var monIds = writes.Where(w => w.Kind == ItemKinds.Mon).Select(w => w.Id).ToList();
        var current = await db.Items
            .Where(i => i.AccountId == account.Id && (i.Kind != ItemKinds.Mon || monIds.Contains(i.Id)))
            .ToDictionaryAsync(i => (i.Kind, i.Id), ct);

        // 3. Versions: identical values are no-ops; otherwise the base version must be the current one.
        var changes = new List<(ParsedWrite Write, Item? Current)>();
        var unchanged = new List<SyncItem>();
        var conflicts = new List<SyncItem>();
        foreach (var write in writes)
        {
            current.TryGetValue((write.Kind, write.Id), out var item);
            if (write.SameAs(item))
            {
                if (item is not null) unchanged.Add(ToSyncItem(item));
            }
            else if (write.BaseVersion != (item?.Version ?? 0))
            {
                conflicts.Add(item is null ? new SyncItem(write.Kind, write.Id, 0, true, null) : ToSyncItem(item));
            }
            else
            {
                changes.Add((write, item));
            }
        }
        if (conflicts.Count > 0) return new GroupResult(group.ClientGroupId, GroupStatus.Conflict, conflicts);

        // 4. Every Pokémon's bytes must already be uploaded to this account.
        var blobs = changes.Select(c => c.Write.Mon?.Blob).OfType<string>().Distinct().ToList();
        if (blobs.Count > 0)
        {
            var known = await db.Blobs.Where(b => b.AccountId == account.Id && blobs.Contains(b.Hash)).Select(b => b.Hash).ToListAsync(ct);
            if (blobs.Except(known).FirstOrDefault() is { } missing)
                return Rejected(group, ErrorCodes.MissingBlob, $"Blob {missing} was not uploaded.");
        }

        // 5. Every live Pokémon sits inside the boxes (the group's own boxes write wins over the stored one).
        var movingIds = changes.Where(c => c.Write.Kind == ItemKinds.Mon).Select(c => c.Write.Id).ToList();
        var placed = changes.Select(c => c.Write).Where(w => w.Mon is not null).ToList();
        var boxesWrite = changes.Select(c => c.Write).FirstOrDefault(w => w.Kind == ItemKinds.Boxes);
        var boxCount = boxesWrite?.Boxes!.Count
            ?? (current.TryGetValue((ItemKinds.Boxes, ItemKinds.SingletonId), out var boxesItem) && boxesItem.Value is { } stored
                ? JsonSerializer.Deserialize<BoxesValue>(stored, ProtocolJson.Options)!.Count
                : (int?)null);
        if (boxCount is { } count)
        {
            if (placed.FirstOrDefault(w => w.Mon!.Box >= count) is { } outside)
                return Rejected(group, ErrorCodes.InvalidItem, $"Box {outside.Mon!.Box} does not exist.");
            // Mons this group writes were judged above by their new place; everyone else stays where they are.
            if (boxesWrite is not null && await db.Items.AnyAsync(i => i.AccountId == account.Id && i.Kind == ItemKinds.Mon
                    && !i.Deleted && i.Box >= count && !movingIds.Contains(i.Id), ct))
                return Rejected(group, ErrorCodes.InvalidItem, "Move the Pokémon out of the boxes you're removing first.");
        }

        // 6. One Pokémon per slot, judged on the state after the whole group (so swaps work).
        var targets = new Dictionary<(int, int), ParsedWrite>();
        foreach (var write in placed)
        {
            if (!targets.TryAdd((write.Mon!.Box, write.Mon.Slot), write))
                return Rejected(group, ErrorCodes.InvalidItem, $"Two Pokémon target box {write.Mon.Box} slot {write.Mon.Slot}.");
        }
        if (targets.Count > 0)
        {
            var targetBoxes = targets.Keys.Select(k => (int?)k.Item1).Distinct().ToList();
            var occupants = await db.Items.AsNoTracking()
                .Where(i => i.AccountId == account.Id && i.Kind == ItemKinds.Mon && targetBoxes.Contains(i.Box))
                .ToListAsync(ct);
            var taken = occupants
                .Where(i => !movingIds.Contains(i.Id) && targets.ContainsKey((i.Box!.Value, i.Slot!.Value)))
                .Select(ToSyncItem).ToList();
            if (taken.Count > 0)
                return new GroupResult(group.ClientGroupId, GroupStatus.SlotTaken, taken, ErrorCodes.SlotTaken, "Another Pokémon already sits there.");
        }

        // 7. Fair use.
        var growth = changes.Count(c => c.Write.Mon is not null && (c.Current is null || c.Current.Deleted))
                   - changes.Count(c => c.Write.Kind == ItemKinds.Mon && c.Write.Deleted && c.Current is { Deleted: false });
        if (growth > 0)
        {
            var live = await db.Items.CountAsync(i => i.AccountId == account.Id && i.Kind == ItemKinds.Mon && !i.Deleted, ct);
            if (live + growth > options.Value.MaxLiveMonsPerAccount)
                return Rejected(group, ErrorCodes.QuotaExceeded, $"A Bank holds at most {options.Value.MaxLiveMonsPerAccount} Pokémon.");
        }

        // 8. Stage.
        var now = Now;
        var items = new List<SyncItem>(unchanged);
        foreach (var (write, existing) in changes)
        {
            var item = existing ?? db.Items.Add(new Item { AccountId = account.Id, Kind = write.Kind, Id = write.Id }).Entity;
            // A released mon's tombstone keeps its bytes and last value so the web Bank can undo the release
            // for a while (TombstonePruner forgets them later); devices still get a plain tombstone.
            var released = write.Deleted && write.Kind == ItemKinds.Mon && existing is { Deleted: false } && options.Value.ReleasedRetentionDays > 0;
            item.ReleasedValue = released ? existing!.Value : null;
            item.Blob = released ? existing!.Blob : write.Mon?.Blob;
            item.Version = ++account.Version;
            item.Deleted = write.Deleted;
            item.Value = write.Deleted ? null : write.ValueJson;
            item.Box = write.Mon?.Box;
            item.Slot = write.Mon?.Slot;
            item.UpdatedUtc = now;
            items.Add(ToSyncItem(item));
        }
        return new GroupResult(group.ClientGroupId, GroupStatus.Accepted, items);
    }

    /// <summary>
    /// A cursor ahead of the server means the server lost history (restored from an older backup): the epoch
    /// rotates so every other device on that history resyncs too, even after versions catch up again.
    /// A cursor from another epoch must resync. Since 0 (a full download) is always allowed.
    /// </summary>
    private async Task CheckCursorAsync(Account account, long since, string? epoch, CancellationToken ct)
    {
        if (since < 0) throw ApiException.BadRequest(ErrorCodes.InvalidRequest, "since is 0 or more.");
        if (since > account.Version)
        {
            var rotated = Account.NewEpoch();
            await db.Accounts.Where(a => a.Id == account.Id).ExecuteUpdateAsync(s => s.SetProperty(a => a.Epoch, rotated), ct);
            account.Epoch = rotated;
            db.Entry(account).Property(a => a.Epoch).IsModified = false;
            throw ResyncRequired();
        }
        if (since > 0 && epoch is not null && epoch != account.Epoch) throw ResyncRequired();
    }

    private static ApiException ResyncRequired() =>
        new(StatusCodes.Status410Gone, ErrorCodes.ResyncRequired, "This device must download the whole Bank again.");

    private async Task SaveAsync(CancellationToken ct)
    {
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw new ApiException(StatusCodes.Status503ServiceUnavailable, ErrorCodes.Busy, "The Bank changed while saving. Retry the push.");
        }
    }

    private Task TouchDeviceAsync(Guid deviceId, CancellationToken ct)
    {
        var now = Now;
        return db.Devices.Where(d => d.Id == deviceId).ExecuteUpdateAsync(s => s.SetProperty(d => d.LastSyncUtc, now), ct);
    }

    private static GroupResult Rejected(ChangeGroup group, string code, string message) =>
        new(group.ClientGroupId, GroupStatus.Rejected, [], code, message);
}
