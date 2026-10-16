using System.Net;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using PKForge.Sync.Protocol;
using PKForge.Sync.Server.Accounts;
using PKForge.Sync.Server.Sync;
using PKForge.Sync.Server.Tests.Infrastructure;
using PKForge.Sync.Server.Web;

namespace PKForge.Sync.Server.Tests;

/// <summary>IAccountWritePolicy: the one place a deployment can stop an account changing its Bank.</summary>
public sealed class WritePolicyTests
{
    private static readonly TimeSpan Wait = TimeSpan.FromSeconds(5);

    private sealed class DenyWrites(string? reason) : IAccountWritePolicy
    {
        public Task<WriteDecision> CheckAsync(Guid accountId, CancellationToken ct) => Task.FromResult(new WriteDecision(false, reason));
    }

    private static HttpClient WithPolicy(SyncServerFactory server, HttpClient client, IAccountWritePolicy policy, out IDisposable host)
    {
        var factory = server.WithWebHostBuilder(b => b.ConfigureServices(s =>
        {
            s.RemoveAll<IAccountWritePolicy>();
            s.AddSingleton(policy);
        }));
        host = factory;
        var denied = factory.CreateClient();
        denied.DefaultRequestHeaders.Add(SyncProtocol.VersionHeader, "1");
        denied.DefaultRequestHeaders.Authorization = client.DefaultRequestHeaders.Authorization;
        return denied;
    }

    [Fact]
    public async Task TheDefaultPolicyAllowsEveryPush()
    {
        using var server = new SyncServerFactory();
        var (client, _) = await server.RegisterAsync();

        Assert.IsType<AllowAllWrites>(server.Services.GetRequiredService<IAccountWritePolicy>());
        var blob = await client.UploadAsync(Mons.Bytes(1));
        var result = await client.PushAsync(Mons.Group(Mons.Write(Guid.NewGuid(), 0, 0, blob)));

        Assert.Equal(GroupStatus.Accepted, Assert.Single(result.Results).Status);
    }

    [Fact]
    public async Task ADenyingPolicyRefusesPushesWithItsReasonButReadsStillWork()
    {
        using var server = new SyncServerFactory();
        var (client, _) = await server.RegisterAsync();
        var blob = await client.UploadAsync(Mons.Bytes(1));
        await client.PushAsync(Mons.Group(Mons.Write(Guid.NewGuid(), 0, 0, blob)));
        var denied = WithPolicy(server, client, new DenyWrites("Storage quota reached."), out var host);
        using var _ = host;

        var push = await denied.PostJsonAsync("/v1/sync/push", new PushRequest([Mons.Group()]));
        var changes = await denied.GetAsync("/v1/sync/changes?since=0");
        var download = await denied.GetAsync($"/v1/blobs/{blob}");

        Assert.Equal(HttpStatusCode.Forbidden, push.StatusCode);
        var error = await push.ReadErrorAsync();
        Assert.Equal(ErrorCodes.WritesDisabled, error.Code);
        Assert.Equal("Storage quota reached.", error.Message);
        Assert.Equal(HttpStatusCode.OK, changes.StatusCode);
        Assert.Single((await changes.ReadAsync<ChangesResponse>()).Items);
        Assert.Equal(HttpStatusCode.OK, download.StatusCode);
    }

    [Fact]
    public async Task ADenialWithoutAReasonUsesTheNeutralMessage()
    {
        using var server = new SyncServerFactory();
        var (client, _) = await server.RegisterAsync();
        var denied = WithPolicy(server, client, new DenyWrites(null), out var host);
        using var _ = host;

        var push = await denied.PostJsonAsync("/v1/sync/push", new PushRequest([Mons.Group()]));

        Assert.Equal(HttpStatusCode.Forbidden, push.StatusCode);
        Assert.Equal(SyncService.WritesDisabledMessage, (await push.ReadErrorAsync()).Message);
    }

    [Fact]
    public async Task AccountChangedReachesTheAccountsStreams()
    {
        using var server = new SyncServerFactory();
        var (client, key) = await server.RegisterAsync();
        await using var stream = await EventStream.OpenAsync(client);
        Assert.Equal("ready", (await stream.NextAsync(Wait)).Name);

        server.Services.GetRequiredService<AccountEvents>().AccountChanged(key.AccountId);

        Assert.Equal("account", (await stream.NextAsync(Wait)).Name);
    }
}
