using System.Net;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PKForge.Sync.Protocol;
using PKForge.Sync.Server.Data;
using PKForge.Sync.Server.Tests.Infrastructure;

namespace PKForge.Sync.Server.Tests;

public sealed class AccountTests
{
    [Fact]
    public async Task FirstAccountIsAdminAndRegistrationThenCloses()
    {
        using var server = new SyncServerFactory();
        server.Settings["Sync:AllowRegistration"] = "false";

        var info = await (await server.Client().GetAsync("/v1/server-info")).ReadAsync<ServerInfo>();
        Assert.True(info.RegistrationOpen);

        var (_, first) = await server.RegisterAsync("admin@example.com");
        var second = await server.Client().PostJsonAsync("/v1/accounts", new RegisterRequest("misty@example.com", TestApi.Password, "Phone"));

        Assert.Equal(HttpStatusCode.Forbidden, second.StatusCode);
        Assert.Equal(ErrorCodes.RegistrationClosed, (await second.ReadErrorAsync()).Code);
        info = await (await server.Client().GetAsync("/v1/server-info")).ReadAsync<ServerInfo>();
        Assert.False(info.RegistrationOpen);
        using var scope = server.Services.CreateScope();
        var account = await scope.ServiceProvider.GetRequiredService<SyncDbContext>().Accounts.SingleAsync();
        Assert.Equal(first.AccountId, account.Id);
        Assert.True(account.IsAdmin);
    }

    [Fact]
    public async Task EmailIsCaseAndSpaceInsensitive()
    {
        using var server = new SyncServerFactory();
        await server.RegisterAsync("Ash@Example.com");

        var duplicate = await server.Client().PostJsonAsync("/v1/accounts", new RegisterRequest("  ash@example.COM ", TestApi.Password, "Phone"));
        var login = await server.Client().PostJsonAsync("/v1/auth/login", new LoginRequest(" ASH@example.com", TestApi.Password, "Deck"));

        Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);
        Assert.Equal(ErrorCodes.EmailTaken, (await duplicate.ReadErrorAsync()).Code);
        login.EnsureSuccessStatusCode();
    }

    [Theory]
    [InlineData("not-an-email", TestApi.Password, "Phone", ErrorCodes.InvalidRequest)]
    [InlineData("ash@example.com", "short", "Phone", ErrorCodes.WeakPassword)]
    [InlineData("ash@example.com", TestApi.Password, "  ", ErrorCodes.InvalidRequest)]
    public async Task RegistrationValidatesInput(string email, string password, string device, string code)
    {
        using var server = new SyncServerFactory();
        var response = await server.Client().PostJsonAsync("/v1/accounts", new RegisterRequest(email, password, device));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(code, (await response.ReadErrorAsync()).Code);
    }

    [Fact]
    public async Task MalformedJsonIsAnInvalidRequest()
    {
        using var server = new SyncServerFactory();
        var response = await server.Client().PostAsync("/v1/accounts", new StringContent("{oops", System.Text.Encoding.UTF8, "application/json"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(ErrorCodes.InvalidRequest, (await response.ReadErrorAsync()).Code);
    }

    [Fact]
    public async Task LoginWithWrongPasswordFails()
    {
        using var server = new SyncServerFactory();
        await server.RegisterAsync();

        var response = await server.Client().PostJsonAsync("/v1/auth/login", new LoginRequest("ash@example.com", "wrong-password", "Deck"));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal(ErrorCodes.InvalidCredentials, (await response.ReadErrorAsync()).Code);
    }

    [Fact]
    public async Task EachLoginAddsADeviceWithItsOwnKey()
    {
        using var server = new SyncServerFactory();
        var (phone, phoneKey) = await server.RegisterAsync(device: "Phone");
        server.Time.Advance(TimeSpan.FromSeconds(1));
        var login = await server.Client().PostJsonAsync("/v1/auth/login", new LoginRequest("ash@example.com", TestApi.Password, "Steam Deck"));
        var deckKey = await login.ReadAsync<DeviceKeyResponse>();

        var devices = await (await server.AuthedClient(deckKey.DeviceKey).GetAsync("/v1/devices")).ReadAsync<List<DeviceInfo>>();

        Assert.Equal(phoneKey.AccountId, deckKey.AccountId);
        Assert.NotEqual(phoneKey.DeviceKey, deckKey.DeviceKey);
        Assert.Equal(["Phone", "Steam Deck"], devices.Select(d => d.Name));
        Assert.Equal([false, true], devices.Select(d => d.IsCurrent));
    }

    [Fact]
    public async Task UnknownOrMissingKeyIsUnauthorized()
    {
        using var server = new SyncServerFactory();
        Assert.Equal(HttpStatusCode.Unauthorized, (await server.Client().GetAsync("/v1/devices")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await server.AuthedClient("made-up").GetAsync("/v1/devices")).StatusCode);
    }

    [Fact]
    public async Task RemovedDeviceLosesAccess()
    {
        using var server = new SyncServerFactory();
        var (phone, _) = await server.RegisterAsync();
        var deckKey = await (await server.Client().PostJsonAsync("/v1/auth/login", new LoginRequest("ash@example.com", TestApi.Password, "Deck"))).ReadAsync<DeviceKeyResponse>();

        var removed = await phone.DeleteAsync($"/v1/devices/{deckKey.DeviceId}");

        Assert.Equal(HttpStatusCode.NoContent, removed.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await server.AuthedClient(deckKey.DeviceKey).GetAsync("/v1/devices")).StatusCode);
    }

    [Fact]
    public async Task CannotRemoveAnotherAccountsDevice()
    {
        using var server = new SyncServerFactory();
        var (ash, _) = await server.RegisterAsync("ash@example.com");
        var (_, misty) = await server.RegisterAsync("misty@example.com");

        var response = await ash.DeleteAsync($"/v1/devices/{misty.DeviceId}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await server.AuthedClient(misty.DeviceKey).GetAsync("/v1/devices")).StatusCode);
    }

    [Fact]
    public async Task ChangingPasswordCanSignOutOtherDevices()
    {
        using var server = new SyncServerFactory();
        var (phone, _) = await server.RegisterAsync();
        var deck = await (await server.Client().PostJsonAsync("/v1/auth/login", new LoginRequest("ash@example.com", TestApi.Password, "Deck"))).ReadAsync<DeviceKeyResponse>();

        var wrong = await phone.PostJsonAsync("/v1/auth/password", new ChangePasswordRequest("nope-nope", "charizard99", true));
        var changed = await phone.PostJsonAsync("/v1/auth/password", new ChangePasswordRequest(TestApi.Password, "charizard99", true));

        Assert.Equal(HttpStatusCode.Unauthorized, wrong.StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, changed.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await server.AuthedClient(deck.DeviceKey).GetAsync("/v1/devices")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await phone.GetAsync("/v1/devices")).StatusCode);
        var relogin = await server.Client().PostJsonAsync("/v1/auth/login", new LoginRequest("ash@example.com", "charizard99", "Deck"));
        relogin.EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task LoginAttemptsAreRateLimited()
    {
        using var server = new SyncServerFactory();
        server.Settings["Sync:AuthRequestsPerMinute"] = "3";
        var client = server.Client();

        HttpResponseMessage last = null!;
        for (var i = 0; i < 4; i++)
            last = await client.PostJsonAsync("/v1/auth/login", new LoginRequest("ash@example.com", "wrong-password", "Deck"));

        Assert.Equal(HttpStatusCode.TooManyRequests, last.StatusCode);
        Assert.Equal(ErrorCodes.RateLimited, (await last.ReadErrorAsync()).Code);
    }
}
