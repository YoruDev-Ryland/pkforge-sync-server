using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using PKForge.Sync.Protocol;
using Microsoft.Extensions.DependencyInjection;
using PKForge.Sync.Server.Tests.Infrastructure;
using PKForge.Sync.Server.Web;

namespace PKForge.Sync.Server.Tests;

/// <summary>GET /v1/sync/events: change notifications for the web Bank.</summary>
public class EventsTests
{
    private static readonly TimeSpan Wait = TimeSpan.FromSeconds(5);

    private static async Task<HttpClient> LoginAsync(SyncServerFactory server, string device, string email = "ash@example.com")
    {
        var response = await server.Client().PostJsonAsync("/v1/auth/login", new LoginRequest(email, TestApi.Password, device));
        response.EnsureSuccessStatusCode();
        return server.AuthedClient((await response.ReadAsync<DeviceKeyResponse>()).DeviceKey);
    }

    [Fact]
    public async Task ReadyComesFirstWithTheCurrentVersionAndStreamingHeaders()
    {
        using var server = new SyncServerFactory();
        var (phone, _) = await server.RegisterAsync();
        var pushed = await phone.PushAsync(Mons.Group(Mons.Boxes(2)));

        await using var stream = await EventStream.OpenAsync(phone);
        Assert.Equal("text/event-stream", stream.Response.Content.Headers.ContentType?.MediaType);
        Assert.Contains("no-cache", stream.Response.Headers.CacheControl?.ToString());
        Assert.Contains("no-transform", stream.Response.Headers.CacheControl?.ToString());
        Assert.Equal("no", stream.Response.Headers.GetValues("X-Accel-Buffering").Single());

        var ready = await stream.NextAsync(Wait);
        Assert.Equal("ready", ready.Name);
        Assert.Equal(pushed.Version, ready.Json.GetProperty("version").GetInt64());
    }

    [Fact]
    public async Task APushFromOneDeviceNudgesTheOthersButNotOtherAccounts()
    {
        using var server = new SyncServerFactory();
        var (phone, _) = await server.RegisterAsync();
        var web = await LoginAsync(server, "Firefox");
        var (misty, _) = await server.RegisterAsync("misty@example.com");

        await using var webStream = await EventStream.OpenAsync(web);
        await using var mistyStream = await EventStream.OpenAsync(misty);
        Assert.Equal("ready", (await webStream.NextAsync(Wait)).Name);
        Assert.Equal("ready", (await mistyStream.NextAsync(Wait)).Name);

        var pushed = await phone.PushAsync(Mons.Group(Mons.Boxes(2)));
        var bank = await webStream.NextAsync(Wait);
        Assert.Equal("bank", bank.Name);
        Assert.Equal(pushed.Version, bank.Json.GetProperty("version").GetInt64());

        await mistyStream.ExpectNothingAsync(TimeSpan.FromMilliseconds(500));
    }

    [Fact]
    public async Task APushThatStoresNothingSendsNoNudge()
    {
        using var server = new SyncServerFactory();
        var (phone, _) = await server.RegisterAsync();
        var web = await LoginAsync(server, "Firefox");
        var first = await phone.PushAsync(Mons.Group(Mons.Boxes(2)));

        await using var stream = await EventStream.OpenAsync(web);
        Assert.Equal("ready", (await stream.NextAsync(Wait)).Name);
        // The same value again is a no-op: the account version does not move.
        await phone.PushAsync(Mons.Group(Mons.Boxes(2, first.Version)));
        await stream.ExpectNothingAsync(TimeSpan.FromMilliseconds(500));
    }

    [Fact]
    public async Task SendsAHeartbeatEveryInterval()
    {
        using var server = new SyncServerFactory();
        server.Settings["Sync:EventHeartbeatSeconds"] = "1";
        var (phone, _) = await server.RegisterAsync();

        await using var stream = await EventStream.OpenAsync(phone);
        Assert.Equal("ready", (await stream.NextAsync(Wait)).Name);
        server.Time.Advance(TimeSpan.FromSeconds(1));
        Assert.Equal(": ping", await stream.NextCommentAsync(Wait));
        server.Time.Advance(TimeSpan.FromSeconds(1));
        Assert.Equal(": ping", await stream.NextCommentAsync(Wait));
    }

    [Fact]
    public async Task RemovingADeviceEndsItsStreamAndTellsTheOthers()
    {
        using var server = new SyncServerFactory();
        var (phone, _) = await server.RegisterAsync();
        var web = await LoginAsync(server, "Firefox");
        var webId = (await (await web.GetAsync("/v1/devices")).ReadAsync<List<DeviceInfo>>()).Single(d => d.IsCurrent).Id;

        await using var phoneStream = await EventStream.OpenAsync(phone);
        await using var webStream = await EventStream.OpenAsync(web);
        Assert.Equal("ready", (await phoneStream.NextAsync(Wait)).Name);
        Assert.Equal("ready", (await webStream.NextAsync(Wait)).Name);

        Assert.Equal(HttpStatusCode.NoContent, (await phone.DeleteAsync($"/v1/devices/{webId}")).StatusCode);

        Assert.Equal("device-removed", (await webStream.NextAsync(Wait)).Name);
        Assert.True(await webStream.EndedAsync(Wait));
        Assert.Equal("devices", (await phoneStream.NextAsync(Wait)).Name);
    }

    [Fact]
    public async Task ANewDeviceTellsTheAccountsStreams()
    {
        using var server = new SyncServerFactory();
        var (phone, _) = await server.RegisterAsync();
        await using var stream = await EventStream.OpenAsync(phone);
        Assert.Equal("ready", (await stream.NextAsync(Wait)).Name);

        await LoginAsync(server, "Firefox");
        Assert.Equal("devices", (await stream.NextAsync(Wait)).Name);

        var code = await (await phone.PostAsync("/v1/pairing", null)).ReadAsync<PairingCodeResponse>();
        (await server.Client().PostJsonAsync("/v1/pairing/redeem", new RedeemPairingRequest(code.Code, "Switch"))).EnsureSuccessStatusCode();
        Assert.Equal("devices", (await stream.NextAsync(Wait)).Name);
    }

    [Fact]
    public async Task SigningOutOtherDevicesWithAPasswordChangeEndsTheirStreams()
    {
        using var server = new SyncServerFactory();
        var (phone, _) = await server.RegisterAsync();
        var web = await LoginAsync(server, "Firefox");
        await using var phoneStream = await EventStream.OpenAsync(phone);
        await using var webStream = await EventStream.OpenAsync(web);
        Assert.Equal("ready", (await phoneStream.NextAsync(Wait)).Name);
        Assert.Equal("ready", (await webStream.NextAsync(Wait)).Name);

        (await phone.PostJsonAsync("/v1/auth/password", new ChangePasswordRequest(TestApi.Password, "new-password-1", true))).EnsureSuccessStatusCode();

        Assert.Equal("device-removed", (await webStream.NextAsync(Wait)).Name);
        Assert.True(await webStream.EndedAsync(Wait));
        Assert.Equal("devices", (await phoneStream.NextAsync(Wait)).Name);
    }

    [Fact]
    public async Task SavingPreferencesTellsTheAccountsOtherStreams()
    {
        using var server = new SyncServerFactory();
        var (phone, _) = await server.RegisterAsync();
        var web = await LoginAsync(server, "Firefox");
        await using var webStream = await EventStream.OpenAsync(web);
        Assert.Equal("ready", (await webStream.NextAsync(Wait)).Name);

        (await phone.PutAsJsonAsync("/v1/account/preferences", new { theme = "fire" })).EnsureSuccessStatusCode();
        Assert.Equal("prefs", (await webStream.NextAsync(Wait)).Name);
    }

    [Fact]
    public async Task SigningOutTheWebSessionEndsItsStreams()
    {
        using var server = new SyncServerFactory();
        var (phone, _) = await server.RegisterAsync();
        var web = await LoginAsync(server, "Firefox");
        await using var phoneStream = await EventStream.OpenAsync(phone);
        await using var webStream = await EventStream.OpenAsync(web);
        Assert.Equal("ready", (await phoneStream.NextAsync(Wait)).Name);
        Assert.Equal("ready", (await webStream.NextAsync(Wait)).Name);

        Assert.Equal(HttpStatusCode.NoContent, (await web.DeleteAsync("/v1/web/session")).StatusCode);
        Assert.Equal("device-removed", (await webStream.NextAsync(Wait)).Name);
        Assert.True(await webStream.EndedAsync(Wait));
        Assert.Equal("devices", (await phoneStream.NextAsync(Wait)).Name);
    }

    [Fact]
    public async Task DeletingTheAccountEndsEveryStream()
    {
        using var server = new SyncServerFactory();
        var (phone, _) = await server.RegisterAsync();
        var web = await LoginAsync(server, "Firefox");
        await using var webStream = await EventStream.OpenAsync(web);
        Assert.Equal("ready", (await webStream.NextAsync(Wait)).Name);

        var response = await phone.SendAsync(new HttpRequestMessage(HttpMethod.Delete, "/v1/account")
        {
            Content = JsonContent.Create(new DeleteAccountRequest(TestApi.Password), options: ProtocolJson.Options),
        });
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal("device-removed", (await webStream.NextAsync(Wait)).Name);
        Assert.True(await webStream.EndedAsync(Wait));
    }

    [Fact]
    public async Task ADeviceKeepsAtMostEightStreamsAndTheOldestCloses()
    {
        using var server = new SyncServerFactory();
        var (phone, key) = await server.RegisterAsync();
        var web = await LoginAsync(server, "Firefox");
        var events = server.Services.GetRequiredService<AccountEvents>();
        await using var other = await EventStream.OpenAsync(web);
        Assert.Equal("ready", (await other.NextAsync(Wait)).Name);

        var streams = new List<EventStream>();
        try
        {
            for (var i = 0; i < AccountEvents.MaxStreamsPerDevice; i++)
            {
                streams.Add(await EventStream.OpenAsync(phone));
                Assert.Equal("ready", (await streams[^1].NextAsync(Wait)).Name);
            }
            var ninth = await EventStream.OpenAsync(phone);
            streams.Add(ninth);
            Assert.Equal("ready", (await ninth.NextAsync(Wait)).Name);

            Assert.True(await streams[0].EndedAsync(Wait));
            var deadline = DateTime.UtcNow + Wait;
            while (events.Count(key.AccountId) > AccountEvents.MaxStreamsPerDevice + 1 && DateTime.UtcNow < deadline) await Task.Delay(20);
            Assert.Equal(AccountEvents.MaxStreamsPerDevice + 1, events.Count(key.AccountId)); // 8 for the phone, 1 for the browser

            await phone.PushAsync(Mons.Group(Mons.Boxes(2)));
            Assert.Equal("bank", (await streams[1].NextAsync(Wait)).Name);
            Assert.Equal("bank", (await ninth.NextAsync(Wait)).Name);
            Assert.Equal("bank", (await other.NextAsync(Wait)).Name);
        }
        finally
        {
            foreach (var s in streams) await s.DisposeAsync();
        }
    }

    [Fact]
    public async Task ClosingTheStreamUnsubscribes()
    {
        using var server = new SyncServerFactory();
        var (phone, key) = await server.RegisterAsync();
        var events = server.Services.GetRequiredService<AccountEvents>();
        var stream = await EventStream.OpenAsync(phone);
        Assert.Equal("ready", (await stream.NextAsync(Wait)).Name);
        Assert.Equal(1, events.Count(key.AccountId));

        await stream.DisposeAsync();
        var deadline = DateTime.UtcNow + Wait;
        while (events.Count(key.AccountId) > 0 && DateTime.UtcNow < deadline) await Task.Delay(20);
        Assert.Equal(0, events.Count(key.AccountId));
    }

    [Fact]
    public async Task RequiresTheProtocolHeaderAndAuthentication()
    {
        using var server = new SyncServerFactory();
        var (phone, _) = await server.RegisterAsync();

        var bare = server.CreateClient();
        bare.DefaultRequestHeaders.Authorization = phone.DefaultRequestHeaders.Authorization;
        Assert.Equal(HttpStatusCode.UpgradeRequired, (await bare.GetAsync("/v1/sync/events")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await server.Client().GetAsync("/v1/sync/events")).StatusCode);
    }

    [Fact]
    public async Task UnverifiedAccountsAreRefusedWhenVerificationIsRequired()
    {
        using var server = new SyncServerFactory();
        server.Settings["Sync:RequireEmailVerification"] = "true";
        var (phone, _) = await server.RegisterAsync();
        var response = await phone.GetAsync("/v1/sync/events");
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal(ErrorCodes.EmailNotVerified, (await response.ReadErrorAsync()).Code);
    }
}

/// <summary>A server-sent event stream read line by line.</summary>
internal sealed class EventStream : IAsyncDisposable
{
    public sealed record Event(string Name, string Data)
    {
        public JsonElement Json => JsonDocument.Parse(Data).RootElement;
    }

    public HttpResponseMessage Response { get; }
    private readonly StreamReader reader;
    private Task<string?>? pending;

    private EventStream(HttpResponseMessage response, Stream body)
    {
        Response = response;
        reader = new StreamReader(body);
    }

    public static async Task<EventStream> OpenAsync(HttpClient client)
    {
        var response = await client.SendAsync(new HttpRequestMessage(HttpMethod.Get, "/v1/sync/events"), HttpCompletionOption.ResponseHeadersRead);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return new EventStream(response, await response.Content.ReadAsStreamAsync());
    }

    /// <summary>The next line, or throws <see cref="TimeoutException"/>. A line read that timed out is kept for the next call.</summary>
    private async Task<string?> LineAsync(TimeSpan timeout)
    {
        pending ??= reader.ReadLineAsync();
        var done = await Task.WhenAny(pending, Task.Delay(timeout));
        if (done != pending) throw new TimeoutException();
        var line = await pending;
        pending = null;
        return line;
    }

    /// <summary>The next event, skipping comments.</summary>
    public async Task<Event> NextAsync(TimeSpan timeout)
    {
        string? name = null, data = null;
        while (true)
        {
            var line = await LineAsync(timeout) ?? throw new EndOfStreamException();
            if (line.Length == 0)
            {
                if (name is not null || data is not null) return new Event(name ?? "message", data ?? "");
                continue;
            }
            if (line.StartsWith(':')) continue;
            if (line.StartsWith("event:")) name = line["event:".Length..].Trim();
            else if (line.StartsWith("data:")) data = line["data:".Length..].Trim();
        }
    }

    /// <summary>The next comment line, skipping blank lines.</summary>
    public async Task<string> NextCommentAsync(TimeSpan timeout)
    {
        while (true)
        {
            var line = await LineAsync(timeout) ?? throw new EndOfStreamException();
            if (line.StartsWith(':')) return line;
            if (line.Length != 0) throw new InvalidOperationException($"Expected a comment, got '{line}'.");
        }
    }

    public async Task ExpectNothingAsync(TimeSpan quiet)
    {
        try
        {
            var line = await LineAsync(quiet);
            Assert.Fail($"Expected silence, got '{line}'.");
        }
        catch (TimeoutException) { }
    }

    /// <summary>True when the server closed the stream (after any remaining blank lines).</summary>
    public async Task<bool> EndedAsync(TimeSpan timeout)
    {
        while (true)
        {
            var line = await LineAsync(timeout);
            if (line is null) return true;
            if (line.Length != 0) return false;
        }
    }

    public ValueTask DisposeAsync()
    {
        reader.Dispose();
        Response.Dispose();
        return ValueTask.CompletedTask;
    }
}
