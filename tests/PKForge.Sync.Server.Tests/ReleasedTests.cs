using System.Net;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using PKForge.Sync.Protocol;
using PKForge.Sync.Server.Sync;
using PKForge.Sync.Server.Tests.Infrastructure;

namespace PKForge.Sync.Server.Tests;

/// <summary>Recently released: a release keeps the mon's bytes and value for a while so the web Bank can undo it.</summary>
public sealed class ReleasedTests
{
    private static readonly Guid A = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000001");
    private static readonly Guid B = Guid.Parse("bbbbbbbb-0000-0000-0000-000000000002");

    private static async Task PruneAsync(SyncServerFactory server)
    {
        using var scope = server.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<TombstonePruner>().PruneAsync(CancellationToken.None);
    }

    private static async Task<ReleasedResponse> ReleasedAsync(HttpClient client)
    {
        var response = await client.GetAsync("/v1/bank/released");
        response.EnsureSuccessStatusCode();
        return await response.ReadAsync<ReleasedResponse>();
    }

    /// <summary>A Bank with A at box 0 slot 0 (v1), released (v2).</summary>
    private static async Task<(HttpClient Client, string Blob)> ReleasedMonAsync(SyncServerFactory server, byte[]? bytes = null)
    {
        var (client, _) = await server.RegisterAsync();
        var blob = await client.UploadAsync(bytes ?? Mons.Bytes(1));
        await client.PushAsync(Mons.Group(Mons.Write(A, 0, 3, blob)));
        await client.PushAsync(Mons.Group(Mons.Delete(A, baseVersion: 1)));
        return (client, blob);
    }

    private static async Task<HttpClient> PairAsync(SyncServerFactory server, HttpClient phone)
    {
        var code = await (await phone.PostAsync("/v1/pairing", null)).ReadAsync<PairingCodeResponse>();
        var key = await (await server.Client().PostJsonAsync("/v1/pairing/redeem", new RedeemPairingRequest(code.Code, "Deck"))).ReadAsync<DeviceKeyResponse>();
        return server.AuthedClient(key.DeviceKey);
    }

    [Fact]
    public async Task ReleasedMonsAreListedNewestFirstWithTheirLastValue()
    {
        using var server = new SyncServerFactory();
        var (client, _) = await server.RegisterAsync();
        var blob = await client.UploadAsync(Mons.Bytes(1));
        await client.PushAsync(Mons.Group(Mons.Write(A, 0, 0, blob), Mons.Write(B, 0, 1, blob, nickname: "Bulby")));  // v1, v2
        await client.PushAsync(Mons.Group(Mons.Delete(A, baseVersion: 1)));                                          // v3
        server.Time.Advance(TimeSpan.FromHours(1));
        await client.PushAsync(Mons.Group(Mons.Delete(B, baseVersion: 2)));                                          // v4

        var released = await ReleasedAsync(client);

        Assert.Equal(30, released.RetentionDays);
        Assert.Equal([B.ToString(), A.ToString()], released.Items.Select(i => i.Id));
        Assert.Equal([4L, 3L], released.Items.Select(i => i.Version));
        Assert.Equal(server.Time.GetUtcNow(), released.Items[0].ReleasedUtc);
        var value = released.Items[0].Value.Deserialize<MonValue>(ProtocolJson.Options)!;
        Assert.Equal((0, 1, blob), (value.Box, value.Slot, value.Blob));
        Assert.Equal("Bulby", value.Info.GetProperty("nickname").GetString());
    }

    [Fact]
    public async Task DevicesStillSeeAPlainTombstone()
    {
        using var server = new SyncServerFactory();
        var (client, blob) = await ReleasedMonAsync(server);

        var tombstone = Assert.Single((await client.ChangesAsync(1)).Items);
        Assert.True(tombstone.Deleted);
        Assert.Null(tombstone.Value);
        Assert.DoesNotContain(blob, JsonSerializer.Serialize(await client.ChangesAsync(0), ProtocolJson.Options));
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/v1/web/mons/{A}/details")).StatusCode);
    }

    [Fact]
    public async Task AKeptBlobDoesNotHoldTheSlotTheBoxOrTheQuota()
    {
        using var server = new SyncServerFactory();
        server.Settings["Sync:MaxLiveMonsPerAccount"] = "1";
        var (client, _) = await server.RegisterAsync();
        var blob = await client.UploadAsync(Mons.Bytes(1));
        await client.PushAsync(Mons.Group(Mons.Boxes(2), Mons.Write(A, 1, 3, blob)));   // v1, v2
        await client.PushAsync(Mons.Group(Mons.Delete(A, baseVersion: 2)));             // v3

        // Its box can go, and it doesn't count as live or sit in its old slot.
        var shrink = await client.PushAsync(Mons.Group(Mons.Boxes(1, baseVersion: 1)));
        Assert.Equal(GroupStatus.Accepted, shrink.Results[0].Status);
        var grow = await client.PushAsync(Mons.Group(Mons.Boxes(2, baseVersion: 4)));
        Assert.Equal(GroupStatus.Accepted, grow.Results[0].Status);
        var reuse = await client.PushAsync(Mons.Group(Mons.Write(B, 1, 3, blob)));
        Assert.Equal(GroupStatus.Accepted, reuse.Results[0].Status);
    }

    [Fact]
    public async Task DeletingAgainChangesNothing()
    {
        using var server = new SyncServerFactory();
        var (client, _) = await ReleasedMonAsync(server);

        var again = await client.PushAsync(Mons.Group(Mons.Delete(A, baseVersion: 2)));

        Assert.Equal(2, again.Version);
        Assert.Single((await ReleasedAsync(client)).Items);
    }

    [Fact]
    public async Task RestoringIsAPushOnTheTombstoneAndOtherDevicesGetTheMonWithItsBytes()
    {
        using var server = new SyncServerFactory();
        var bytes = Mons.Bytes(7);
        var (web, blob) = await ReleasedMonAsync(server, bytes);
        var deck = await PairAsync(server, web);
        Assert.DoesNotContain((await deck.ChangesAsync(0)).Items, i => !i.Deleted);

        // Days later the original bytes are still there (the tombstone keeps them), so the push is valid.
        server.Time.Advance(TimeSpan.FromDays(10));
        await PruneAsync(server);
        var released = Assert.Single((await ReleasedAsync(web)).Items);
        var value = released.Value.Deserialize<MonValue>(ProtocolJson.Options)!;
        var restore = await web.PushAsync(Mons.Group(Mons.Write(A, value.Box, value.Slot, value.Blob, baseVersion: released.Version)));

        Assert.Equal(GroupStatus.Accepted, restore.Results[0].Status);
        Assert.Empty((await ReleasedAsync(web)).Items);

        var item = Assert.Single((await deck.ChangesAsync(2)).Items);
        Assert.False(item.Deleted);
        Assert.Equal(blob, item.Value!.Value.Deserialize<MonValue>(ProtocolJson.Options)!.Blob);
        var download = await deck.GetAsync($"/v1/blobs/{blob}");
        Assert.Equal(HttpStatusCode.OK, download.StatusCode);
        Assert.Equal(bytes, await download.Content.ReadAsByteArrayAsync());
    }

    [Fact]
    public async Task AfterTheWindowTheReleaseIsFinal()
    {
        using var server = new SyncServerFactory();
        var (client, blob) = await ReleasedMonAsync(server);

        server.Time.Advance(TimeSpan.FromDays(31));
        await PruneAsync(server);

        Assert.Empty((await ReleasedAsync(client)).Items);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/v1/blobs/{blob}")).StatusCode);
        var restore = await client.PushAsync(Mons.Group(Mons.Write(A, 0, 3, blob, baseVersion: 2)));
        Assert.Equal(GroupStatus.Rejected, restore.Results[0].Status);
        Assert.Equal(ErrorCodes.MissingBlob, restore.Results[0].Error);
        // The tombstone itself stays for the normal retention.
        Assert.True(Assert.Single((await client.ChangesAsync(1)).Items).Deleted);
    }

    [Fact]
    public async Task OutsideTheWindowButBeforePruningTheListIsAlreadyEmpty()
    {
        using var server = new SyncServerFactory();
        var (client, _) = await ReleasedMonAsync(server);

        server.Time.Advance(TimeSpan.FromDays(30) + TimeSpan.FromMinutes(1));

        Assert.Empty((await ReleasedAsync(client)).Items);
    }

    [Fact]
    public async Task ABlobAnotherMonStillUsesSurvivesTheWindow()
    {
        using var server = new SyncServerFactory();
        var (client, blob) = await ReleasedMonAsync(server);
        await client.PushAsync(Mons.Group(Mons.Write(B, 0, 0, blob)));

        server.Time.Advance(TimeSpan.FromDays(31));
        await PruneAsync(server);

        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync($"/v1/blobs/{blob}")).StatusCode);
    }

    [Fact]
    public async Task ZeroDaysMakesReleasesFinal()
    {
        using var server = new SyncServerFactory();
        server.Settings["Sync:ReleasedRetentionDays"] = "0";
        var (client, blob) = await ReleasedMonAsync(server);

        var released = await ReleasedAsync(client);
        Assert.Equal(0, released.RetentionDays);
        Assert.Empty(released.Items);

        server.Time.Advance(TimeSpan.FromDays(2));
        await PruneAsync(server);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/v1/blobs/{blob}")).StatusCode);
    }

    [Fact]
    public async Task EachAccountSeesOnlyItsOwnReleases()
    {
        using var server = new SyncServerFactory();
        await ReleasedMonAsync(server);
        var (other, _) = await server.RegisterAsync("misty@example.com");

        Assert.Empty((await ReleasedAsync(other)).Items);
    }

    [Fact]
    public async Task TheListNeedsSignInAndTheProtocolHeader()
    {
        using var server = new SyncServerFactory();
        var (client, _) = await server.RegisterAsync();

        Assert.Equal(HttpStatusCode.Unauthorized, (await server.Client().GetAsync("/v1/bank/released")).StatusCode);
        client.DefaultRequestHeaders.Remove(SyncProtocol.VersionHeader);
        Assert.Equal(HttpStatusCode.UpgradeRequired, (await client.GetAsync("/v1/bank/released")).StatusCode);
    }

    [Theory]
    [InlineData("-1")]
    [InlineData("181")]
    public void TheWindowMustFitInsideTombstoneRetention(string days)
    {
        using var server = new SyncServerFactory();
        server.Settings["Sync:ReleasedRetentionDays"] = days;

        Assert.ThrowsAny<Exception>(() => server.Client());
    }

    [Fact]
    public void TheEnvironmentAliasMapsToTheSetting()
    {
        var read = EnvironmentAliases.Read(name => name == "RELEASED_RETENTION_DAYS" ? "7" : null).ToList();
        Assert.Contains(new KeyValuePair<string, string?>("Sync:ReleasedRetentionDays", "7"), read);
    }
}
