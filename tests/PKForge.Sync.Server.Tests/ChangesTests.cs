using System.Net;
using PKForge.Sync.Protocol;
using PKForge.Sync.Server.Tests.Infrastructure;

namespace PKForge.Sync.Server.Tests;

public sealed class ChangesTests
{
    [Fact]
    public async Task ChangesArriveInVersionOrderAcrossDevices()
    {
        using var server = new SyncServerFactory();
        var (phone, _) = await server.RegisterAsync();
        var deckKey = await (await server.Client().PostJsonAsync("/v1/auth/login", new LoginRequest("ash@example.com", TestApi.Password, "Deck"))).ReadAsync<DeviceKeyResponse>();
        var deck = server.AuthedClient(deckKey.DeviceKey);
        var blob = await phone.UploadAsync(Mons.Bytes(1));
        var a = Guid.NewGuid();
        await phone.PushAsync(Mons.Group(Mons.Boxes(1), Mons.Write(a, 0, 0, blob)));
        await phone.PushAsync(Mons.Group(Mons.Write(a, 0, 5, blob, baseVersion: 2)));

        var changes = await deck.ChangesAsync(0);

        Assert.Equal([1L, 3L], changes.Items.Select(i => i.Version));
        Assert.Equal(3, changes.Next);
        Assert.False(changes.HasMore);
        Assert.Empty((await deck.ChangesAsync(changes.Next)).Items);
    }

    [Fact]
    public async Task ChangesArePaged()
    {
        using var server = new SyncServerFactory();
        var (client, _) = await server.RegisterAsync();
        var blob = await client.UploadAsync(Mons.Bytes(1));
        await client.PushAsync(Mons.Group(Enumerable.Range(0, 5).Select(i => Mons.Write(Guid.NewGuid(), 0, i, blob)).ToArray()));

        var first = await client.ChangesAsync(0, limit: 2);
        var second = await client.ChangesAsync(first.Next, limit: 2);
        var third = await client.ChangesAsync(second.Next, limit: 2);

        Assert.Equal([1L, 2L], first.Items.Select(i => i.Version));
        Assert.True(first.HasMore);
        Assert.Equal([3L, 4L], second.Items.Select(i => i.Version));
        Assert.Equal([5L], third.Items.Select(i => i.Version));
        Assert.False(third.HasMore);
    }

    [Fact]
    public async Task CursorAheadOfServerRequiresResync()
    {
        using var server = new SyncServerFactory();
        var (client, _) = await server.RegisterAsync();

        var response = await client.GetAsync("/v1/sync/changes?since=40");

        Assert.Equal(HttpStatusCode.Gone, response.StatusCode);
        Assert.Equal(ErrorCodes.ResyncRequired, (await response.ReadErrorAsync()).Code);
    }

    [Fact]
    public async Task AccountsOnlySeeTheirOwnChanges()
    {
        using var server = new SyncServerFactory();
        var (ash, _) = await server.RegisterAsync("ash@example.com");
        var (misty, _) = await server.RegisterAsync("misty@example.com");
        var blob = await ash.UploadAsync(Mons.Bytes(1));
        await ash.PushAsync(Mons.Group(Mons.Write(Guid.NewGuid(), 0, 0, blob)));

        Assert.Empty((await misty.ChangesAsync(0)).Items);
    }

    [Fact]
    public async Task SyncingRecordsWhenTheDeviceLastSynced()
    {
        using var server = new SyncServerFactory();
        var (client, _) = await server.RegisterAsync();

        await client.ChangesAsync(0);
        var device = Assert.Single(await (await client.GetAsync("/v1/devices")).ReadAsync<List<DeviceInfo>>());

        Assert.Equal(server.Time.GetUtcNow(), device.LastSyncUtc);
    }

    [Fact]
    public async Task MissingOrNegativeCursorIsAnInvalidRequest()
    {
        using var server = new SyncServerFactory();
        var (client, _) = await server.RegisterAsync();

        Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync("/v1/sync/changes")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync("/v1/sync/changes?since=-1")).StatusCode);
    }
}
