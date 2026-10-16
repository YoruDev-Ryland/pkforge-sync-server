using System.Net;
using Microsoft.Extensions.DependencyInjection;
using PKForge.Sync.Protocol;
using PKForge.Sync.Server.Sync;
using PKForge.Sync.Server.Tests.Infrastructure;

namespace PKForge.Sync.Server.Tests;

/// <summary>One walk through every database-backed feature, run against each provider.</summary>
public static class Scenarios
{
    public static async Task FullRoundTripAsync(SyncServerFactory server)
    {
        var (phone, phoneKey) = await server.RegisterAsync("ash@example.com", "Phone");
        var code = await (await phone.PostAsync("/v1/pairing", null)).ReadAsync<PairingCodeResponse>();
        var deckKey = await (await server.Client().PostJsonAsync("/v1/pairing/redeem", new RedeemPairingRequest(code.Code, "Deck"))).ReadAsync<DeviceKeyResponse>();
        var deck = server.AuthedClient(deckKey.DeviceKey);
        Assert.Equal(phoneKey.AccountId, deckKey.AccountId);

        var blob = await phone.UploadAsync(Mons.Bytes(1));
        Guid a = Guid.NewGuid(), b = Guid.NewGuid();
        var created = await phone.PushAsync(Mons.Group(Mons.Boxes(2), Mons.Write(a, 0, 0, blob), Mons.Write(b, 0, 1, blob)));
        Assert.Equal(GroupStatus.Accepted, created.Results[0].Status);

        var swap = await deck.PushAsync(Mons.Group(Mons.Write(a, 0, 1, blob, baseVersion: 2), Mons.Write(b, 0, 0, blob, baseVersion: 3)));
        Assert.Equal(GroupStatus.Accepted, swap.Results[0].Status);

        var stale = await phone.PushAsync(Mons.Group(Mons.Write(a, 0, 1, blob, baseVersion: 2, nickname: "Volt")));
        Assert.Equal(GroupStatus.Conflict, stale.Results[0].Status);

        var taken = await phone.PushAsync(Mons.Group(Mons.Write(Guid.NewGuid(), 0, 0, blob)));
        Assert.Equal(GroupStatus.SlotTaken, taken.Results[0].Status);

        var retryGroup = Mons.Group(Mons.Delete(b, baseVersion: 5));
        await deck.PushAsync(retryGroup);
        Assert.Equal(6, (await deck.PushAsync(retryGroup)).Version);

        var released = await (await phone.GetAsync("/v1/bank/released")).ReadAsync<ReleasedResponse>();
        Assert.Equal((b.ToString(), 6L), (Assert.Single(released.Items).Id, released.Items[0].Version));

        var changes = await phone.ChangesAsync(0);
        Assert.Equal([1L, 4L, 6L], changes.Items.Select(i => i.Version));

        server.Time.Advance(TimeSpan.FromDays(181));
        using (var scope = server.Services.CreateScope())
            await scope.ServiceProvider.GetRequiredService<TombstonePruner>().PruneAsync(CancellationToken.None);
        Assert.Equal(HttpStatusCode.Gone, (await phone.GetAsync("/v1/sync/changes?since=5")).StatusCode);
        Assert.Equal([1L, 4L], (await phone.ChangesAsync(0)).Items.Select(i => i.Version));
    }
}
