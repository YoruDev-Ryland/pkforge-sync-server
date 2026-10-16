using System.Net;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PKForge.Sync.Protocol;
using PKForge.Sync.Server.Data;
using PKForge.Sync.Server.Sync;
using PKForge.Sync.Server.Tests.Infrastructure;

namespace PKForge.Sync.Server.Tests;

public sealed class PrunerTests
{
    private static async Task PruneAsync(SyncServerFactory server)
    {
        using var scope = server.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<TombstonePruner>().PruneAsync(CancellationToken.None);
    }

    [Fact]
    public async Task OldTombstonesArePrunedAndOldCursorsMustResync()
    {
        using var server = new SyncServerFactory();
        var (client, _) = await server.RegisterAsync();
        var blob = await client.UploadAsync(Mons.Bytes(1));
        var a = Guid.NewGuid();
        await client.PushAsync(Mons.Group(Mons.Write(a, 0, 0, blob)));          // v1
        await client.PushAsync(Mons.Group(Mons.Delete(a, baseVersion: 1)));     // v2 tombstone

        server.Time.Advance(TimeSpan.FromDays(181));
        await PruneAsync(server);

        var stale = await client.GetAsync("/v1/sync/changes?since=1");
        Assert.Equal(HttpStatusCode.Gone, stale.StatusCode);
        Assert.Empty((await client.ChangesAsync(0)).Items);
        Assert.Empty((await client.ChangesAsync(2)).Items);
    }

    [Fact]
    public async Task RecentTombstonesStay()
    {
        using var server = new SyncServerFactory();
        var (client, _) = await server.RegisterAsync();
        var blob = await client.UploadAsync(Mons.Bytes(1));
        var a = Guid.NewGuid();
        await client.PushAsync(Mons.Group(Mons.Write(a, 0, 0, blob)));
        await client.PushAsync(Mons.Group(Mons.Delete(a, baseVersion: 1)));

        server.Time.Advance(TimeSpan.FromDays(30));
        await PruneAsync(server);

        Assert.True(Assert.Single((await client.ChangesAsync(1)).Items).Deleted);
    }

    [Fact]
    public async Task UnreferencedBlobsAreRemovedAfterADay()
    {
        using var server = new SyncServerFactory();
        var (client, key) = await server.RegisterAsync();
        var kept = await client.UploadAsync(Mons.Bytes(1));
        var orphan = await client.UploadAsync(Mons.Bytes(2));
        await client.PushAsync(Mons.Group(Mons.Write(Guid.NewGuid(), 0, 0, kept)));

        await PruneAsync(server);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync($"/v1/blobs/{orphan}")).StatusCode);

        server.Time.Advance(TimeSpan.FromDays(2));
        await PruneAsync(server);

        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/v1/blobs/{orphan}")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync($"/v1/blobs/{kept}")).StatusCode);
        Assert.False(File.Exists(Path.Combine(server.DataDirectory, "blobs", key.AccountId.ToString("N"), orphan[..2], orphan)));
    }

    [Fact]
    public async Task OldReceiptsAreRemoved()
    {
        using var server = new SyncServerFactory();
        var (client, _) = await server.RegisterAsync();
        await client.PushAsync(Mons.Group());

        server.Time.Advance(TimeSpan.FromDays(31));
        await PruneAsync(server);

        using var scope = server.Services.CreateScope();
        Assert.Equal(0, await scope.ServiceProvider.GetRequiredService<SyncDbContext>().GroupReceipts.CountAsync());
    }
}
