using System.Net;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PKForge.Sync.Protocol;
using PKForge.Sync.Server.Cli;
using PKForge.Sync.Server.Data;
using PKForge.Sync.Server.Tests.Infrastructure;

namespace PKForge.Sync.Server.Tests;

/// <summary>A server restored from an older backup must make every device re-download, not just the first to notice.</summary>
public sealed class ResyncTests
{
    private static async Task RestoreToVersionAsync(SyncServerFactory server, long version)
    {
        using var scope = server.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SyncDbContext>();
        await db.Items.Where(i => i.Version > version).ExecuteDeleteAsync();
        await db.Accounts.ExecuteUpdateAsync(s => s.SetProperty(a => a.Version, version));
    }

    private static Task<HttpResponseMessage> PullAsync(HttpClient client, long since, string epoch) =>
        client.GetAsync($"/v1/sync/changes?since={since}&epoch={Uri.EscapeDataString(epoch)}");

    private static async Task<(SyncServerFactory Server, HttpClient Phone, HttpClient Deck, string Blob)> TwoDevicesAsync()
    {
        var server = new SyncServerFactory();
        var (phone, _) = await server.RegisterAsync();
        var deckKey = await (await server.Client().PostJsonAsync("/v1/auth/login", new LoginRequest("ash@example.com", TestApi.Password, "Deck"))).ReadAsync<DeviceKeyResponse>();
        var blob = await phone.UploadAsync(Mons.Bytes(1));
        return (server, phone, server.AuthedClient(deckKey.DeviceKey), blob);
    }

    [Fact]
    public async Task ResponsesCarryTheBanksEpoch()
    {
        var (server, phone, _, _) = await TwoDevicesAsync();
        using var _ = server;

        var push = await phone.PushAsync(Mons.Group());
        var changes = await phone.ChangesAsync(0);

        Assert.False(string.IsNullOrEmpty(push.Epoch));
        Assert.Equal(push.Epoch, changes.Epoch);
    }

    [Fact]
    public async Task OneDeviceNoticingARestoreMakesEveryDeviceResync()
    {
        var (server, phone, deck, blob) = await TwoDevicesAsync();
        using var _ = server;
        await phone.PushAsync(Mons.Group(Mons.Write(Guid.NewGuid(), 0, 0, blob), Mons.Write(Guid.NewGuid(), 0, 1, blob), Mons.Write(Guid.NewGuid(), 0, 2, blob)));
        var seen = await deck.ChangesAsync(0);                       // deck: cursor 3, old epoch
        await RestoreToVersionAsync(server, 1);

        var noticed = await phone.PostJsonAsync("/v1/sync/push", new PushRequest([Mons.Group()], Since: 3, Epoch: seen.Epoch));
        var fresh = await phone.ChangesAsync(0);                      // phone re-downloads: new epoch
        await phone.PostJsonAsync("/v1/sync/push", new PushRequest(
            [Mons.Group(Enumerable.Range(3, 5).Select(i => Mons.Write(Guid.NewGuid(), 0, i, blob)).ToArray())], Since: fresh.Next, Epoch: fresh.Epoch));
        var deckPull = await PullAsync(deck, seen.Next, seen.Epoch!);  // server is past 3 again, but on a new epoch

        Assert.Equal(HttpStatusCode.Gone, noticed.StatusCode);
        Assert.NotEqual(seen.Epoch, fresh.Epoch);
        Assert.Equal(HttpStatusCode.Gone, deckPull.StatusCode);
        Assert.Equal(ErrorCodes.ResyncRequired, (await deckPull.ReadErrorAsync()).Code);
    }

    [Fact]
    public async Task PushOnAStaleEpochIsRefusedAndChangesNothing()
    {
        var (server, phone, _, blob) = await TwoDevicesAsync();
        using var _ = server;

        await phone.PushAsync(Mons.Group(Mons.Write(Guid.NewGuid(), 0, 0, blob)));

        var response = await phone.PostJsonAsync("/v1/sync/push", new PushRequest(
            [Mons.Group(Mons.Write(Guid.NewGuid(), 0, 1, blob))], Since: 1, Epoch: "not-the-epoch"));
        var changes = await phone.ChangesAsync(0);

        Assert.Equal(HttpStatusCode.Gone, response.StatusCode);
        Assert.Single(changes.Items);
    }

    [Fact]
    public async Task ForceResyncCommandRotatesTheEpoch()
    {
        var (server, phone, _, blob) = await TwoDevicesAsync();
        using var _ = server;
        await phone.PushAsync(Mons.Group(Mons.Write(Guid.NewGuid(), 0, 0, blob)));
        var before = await phone.ChangesAsync(0);

        var output = new StringWriter();
        var code = await AdminCli.RunAsync(server.Services, AdminCli.Parse(["force-resync", "--all"])!, output);
        var pull = await PullAsync(phone, before.Next, before.Epoch!);

        Assert.Equal(0, code);
        Assert.Equal(HttpStatusCode.Gone, pull.StatusCode);
    }
}
