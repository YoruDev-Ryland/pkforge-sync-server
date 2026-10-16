using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PKForge.Sync.Protocol;
using PKForge.Sync.Server.Admin;
using PKForge.Sync.Server.Data;
using PKForge.Sync.Server.Tests.Infrastructure;

namespace PKForge.Sync.Server.Tests;

public sealed class AdminTests
{
    private static Task<HttpResponseMessage> PutJsonAsync<T>(HttpClient client, string url, T body) =>
        client.PutAsJsonAsync(url, body, ProtocolJson.Options);

    private static Task<HttpResponseMessage> DeleteUserAsync(HttpClient client, Guid id, string email) =>
        client.SendAsync(new HttpRequestMessage(HttpMethod.Delete, $"/v1/admin/users/{id}")
        {
            Content = JsonContent.Create(new AdminDeleteUserRequest(email), options: ProtocolJson.Options),
        });

    private static async Task<ServerInfo> InfoAsync(SyncServerFactory server) =>
        await (await server.Client().GetAsync("/v1/server-info")).ReadAsync<ServerInfo>();

    private static Task<HttpResponseMessage> LoginAsync(SyncServerFactory server, string email, string password) =>
        server.Client().PostJsonAsync("/v1/auth/login", new LoginRequest(email, password, "Phone"));

    [Fact]
    public async Task AccountInfoSaysWhetherTheAccountIsAnAdmin()
    {
        using var server = new SyncServerFactory();
        var (ash, ashKey) = await server.RegisterAsync("ash@example.com");
        var (misty, _) = await server.RegisterAsync("misty@example.com");

        var ashInfo = await (await ash.GetAsync("/v1/account")).ReadAsync<AccountInfo>();
        var mistyInfo = await (await misty.GetAsync("/v1/account")).ReadAsync<AccountInfo>();

        Assert.Equal(new AccountInfo(ashKey.AccountId, "ash@example.com", false, true), ashInfo);
        Assert.False(mistyInfo.IsAdmin);
        Assert.Equal(HttpStatusCode.Unauthorized, (await server.Client().GetAsync("/v1/account")).StatusCode);
    }

    [Fact]
    public async Task EveryAdminEndpointRefusesNonAdmins()
    {
        using var server = new SyncServerFactory();
        var (_, adminKey) = await server.RegisterAsync("ash@example.com");
        var (misty, _) = await server.RegisterAsync("misty@example.com");
        var target = adminKey.AccountId;

        HttpResponseMessage[] responses =
        [
            await misty.GetAsync("/v1/admin/settings"),
            await PutJsonAsync(misty, "/v1/admin/settings", new AdminSettings(false)),
            await misty.GetAsync("/v1/admin/users"),
            await misty.PostJsonAsync("/v1/admin/users", new AdminCreateUserRequest("brock@example.com", true)),
            await misty.PostJsonAsync($"/v1/admin/users/{target}/reset-password", new AdminResetPasswordRequest(false)),
            await PutJsonAsync(misty, $"/v1/admin/users/{target}/admin", new AdminSetRoleRequest(false)),
            await misty.PostAsync($"/v1/admin/users/{target}/sign-out", null),
            await DeleteUserAsync(misty, target, "ash@example.com"),
        ];

        foreach (var response in responses)
        {
            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
            Assert.Equal(ErrorCodes.AdminRequired, (await response.ReadErrorAsync()).Code);
        }
        Assert.Equal(HttpStatusCode.Unauthorized, (await server.Client().GetAsync("/v1/admin/users")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await LoginAsync(server, "ash@example.com", TestApi.Password)).StatusCode);
        Assert.Equal(2, await CountAsync(server, db => db.Accounts));
    }

    [Fact]
    public async Task AdminEndpointsNeedTheProtocolHeader()
    {
        using var server = new SyncServerFactory();
        var (_, key) = await server.RegisterAsync();
        var client = server.CreateClient();
        client.DefaultRequestHeaders.Authorization = new("Bearer", key.DeviceKey);

        Assert.Equal(HttpStatusCode.UpgradeRequired, (await client.GetAsync("/v1/admin/users")).StatusCode);
    }

    [Fact]
    public async Task AnUnverifiedAdminIsHeldBackWhenTheServerRequiresVerification()
    {
        using var server = new SyncServerFactory();
        server.Settings["Sync:RequireEmailVerification"] = "true";
        var (ash, _) = await server.RegisterAsync();

        var response = await ash.GetAsync("/v1/admin/users");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal(ErrorCodes.EmailNotVerified, (await response.ReadErrorAsync()).Code);
    }

    [Fact]
    public async Task UsersListShowsCountsNewestFirst()
    {
        using var server = new SyncServerFactory();
        var (ash, ashKey) = await server.RegisterAsync("ash@example.com");
        server.Time.Advance(TimeSpan.FromDays(1));
        var (misty, _) = await server.RegisterAsync("misty@example.com");
        var blob = await misty.UploadAsync(Mons.Bytes(1));
        Guid kept = Guid.NewGuid(), released = Guid.NewGuid();
        await misty.PushAsync(Mons.Group(Mons.Write(kept, 0, 0, blob), Mons.Write(released, 0, 1, blob)));
        await misty.PushAsync(Mons.Group(Mons.Delete(released, baseVersion: 2)));

        var list = await (await ash.GetAsync("/v1/admin/users")).ReadAsync<AdminUsersResponse>();

        Assert.Equal(2, list.Total);
        Assert.Equal(AdminService.MaxListedUsers, list.Limit);
        Assert.Equal(["misty@example.com", "ash@example.com"], list.Users.Select(u => u.Email));
        var mistyRow = list.Users[0];
        Assert.Equal((false, false, 1, 1), (mistyRow.IsAdmin, mistyRow.EmailVerified, mistyRow.Devices, mistyRow.Mons));
        Assert.Equal(server.Time.GetUtcNow(), mistyRow.LastActiveUtc);
        var ashRow = list.Users[1];
        Assert.Equal((ashKey.AccountId, true, 0, (DateTimeOffset?)null), (ashRow.Id, ashRow.IsAdmin, ashRow.Mons, ashRow.LastActiveUtc));
        Assert.Equal(server.Time.GetUtcNow().AddDays(-1), ashRow.CreatedUtc);
    }

    [Fact]
    public async Task AddUserReturnsAWorkingPasswordOnce()
    {
        using var server = new SyncServerFactory();
        var (ash, _) = await server.RegisterAsync();

        var response = await ash.PostJsonAsync("/v1/admin/users", new AdminCreateUserRequest(" brock@example.com ", true));
        var created = await response.ReadAsync<AdminCreateUserResponse>();
        var again = await ash.PostJsonAsync("/v1/admin/users", new AdminCreateUserRequest("BROCK@example.com", false));
        var bad = await ash.PostJsonAsync("/v1/admin/users", new AdminCreateUserRequest("not an email", false));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(("brock@example.com", true, true, 0), (created.User.Email, created.User.IsAdmin, created.User.EmailVerified, created.User.Devices));
        Assert.Equal(16, created.Password.Length);
        Assert.Equal(HttpStatusCode.OK, (await LoginAsync(server, "brock@example.com", created.Password)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, again.StatusCode);
        Assert.Equal(ErrorCodes.EmailTaken, (await again.ReadErrorAsync()).Code);
        Assert.Equal(HttpStatusCode.BadRequest, bad.StatusCode);
    }

    [Fact]
    public async Task ResetPasswordReturnsAWorkingPasswordAndKeepsDevicesSignedIn()
    {
        using var server = new SyncServerFactory();
        var (ash, _) = await server.RegisterAsync("ash@example.com");
        var (misty, mistyKey) = await server.RegisterAsync("misty@example.com");

        var response = await ash.PostJsonAsync($"/v1/admin/users/{mistyKey.AccountId}/reset-password", new AdminResetPasswordRequest(false));
        var password = (await response.ReadAsync<AdminPasswordResponse>()).Password;

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await LoginAsync(server, "misty@example.com", TestApi.Password)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await LoginAsync(server, "misty@example.com", password)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await misty.GetAsync("/v1/devices")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound,
            (await ash.PostJsonAsync($"/v1/admin/users/{Guid.NewGuid()}/reset-password", new AdminResetPasswordRequest(false))).StatusCode);
    }

    [Fact]
    public async Task ResetPasswordCanSignOutEveryDevice()
    {
        using var server = new SyncServerFactory();
        var (ash, _) = await server.RegisterAsync("ash@example.com");
        var (misty, mistyKey) = await server.RegisterAsync("misty@example.com");

        var response = await ash.PostJsonAsync($"/v1/admin/users/{mistyKey.AccountId}/reset-password", new AdminResetPasswordRequest(true));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await misty.GetAsync("/v1/devices")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await ash.GetAsync("/v1/devices")).StatusCode);
    }

    [Fact]
    public async Task SignOutRevokesEveryDeviceKeyAndPairingCode()
    {
        using var server = new SyncServerFactory();
        var (ash, _) = await server.RegisterAsync("ash@example.com");
        var (phone, mistyKey) = await server.RegisterAsync("misty@example.com", "Phone");
        var deck = server.AuthedClient((await (await server.Client().PostJsonAsync("/v1/auth/login",
            new LoginRequest("misty@example.com", TestApi.Password, "Deck"))).ReadAsync<DeviceKeyResponse>()).DeviceKey);
        var code = await (await phone.PostAsync("/v1/pairing", null)).ReadAsync<PairingCodeResponse>();
        var events = server.Services.GetRequiredService<PKForge.Sync.Server.Web.AccountEvents>();
        using var stream = events.Subscribe(mistyKey.AccountId, mistyKey.DeviceId);

        var response = await ash.PostAsync($"/v1/admin/users/{mistyKey.AccountId}/sign-out", null);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await phone.GetAsync("/v1/devices")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await deck.GetAsync("/v1/devices")).StatusCode);
        Assert.True(stream.Reader.TryRead(out var heard));
        Assert.Equal("device-removed", heard.Kind);
        var redeem = await server.Client().PostJsonAsync("/v1/pairing/redeem", new RedeemPairingRequest(code.Code, "Laptop"));
        Assert.Equal(HttpStatusCode.BadRequest, redeem.StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await LoginAsync(server, "misty@example.com", TestApi.Password)).StatusCode);
    }

    [Fact]
    public async Task AdminsCanBeMadeAndRemovedButNotTheirOwn()
    {
        using var server = new SyncServerFactory();
        var (ash, ashKey) = await server.RegisterAsync("ash@example.com");
        var (misty, mistyKey) = await server.RegisterAsync("misty@example.com");

        var made = await PutJsonAsync(ash, $"/v1/admin/users/{mistyKey.AccountId}/admin", new AdminSetRoleRequest(true));
        Assert.True((await made.ReadAsync<AdminUser>()).IsAdmin);
        Assert.Equal(HttpStatusCode.OK, (await misty.GetAsync("/v1/admin/users")).StatusCode);

        var self = await PutJsonAsync(ash, $"/v1/admin/users/{ashKey.AccountId}/admin", new AdminSetRoleRequest(false));
        Assert.Equal(HttpStatusCode.BadRequest, self.StatusCode);
        Assert.Equal(ErrorCodes.SelfChange, (await self.ReadErrorAsync()).Code);

        var removed = await PutJsonAsync(misty, $"/v1/admin/users/{ashKey.AccountId}/admin", new AdminSetRoleRequest(false));
        Assert.False((await removed.ReadAsync<AdminUser>()).IsAdmin);
        Assert.Equal(HttpStatusCode.Forbidden, (await ash.GetAsync("/v1/admin/users")).StatusCode);
        Assert.False((await (await ash.GetAsync("/v1/account")).ReadAsync<AccountInfo>()).IsAdmin);
    }

    [Fact]
    public async Task TheLastAdminCanNeitherLoseAdminNorBeDeleted()
    {
        using var server = new SyncServerFactory();
        var (_, ashKey) = await server.RegisterAsync("ash@example.com");
        _ = await server.RegisterAsync("misty@example.com");
        using var scope = server.Services.CreateScope();
        var admins = scope.ServiceProvider.GetRequiredService<AdminService>();
        var someoneElse = Guid.NewGuid(); // the API can't reach this (the caller is always an admin); the service still guards it

        var demote = await Assert.ThrowsAsync<PKForge.Sync.Server.Http.ApiException>(() =>
            admins.SetAdminAsync(someoneElse, ashKey.AccountId, false, CancellationToken.None));
        var delete = await Assert.ThrowsAsync<PKForge.Sync.Server.Http.ApiException>(() =>
            admins.DeleteUserAsync(someoneElse, ashKey.AccountId, "ash@example.com", CancellationToken.None));

        Assert.Equal((409, ErrorCodes.LastAdmin), (demote.Status, demote.Code));
        Assert.Equal((409, ErrorCodes.LastAdmin), (delete.Status, delete.Code));
        Assert.Equal(1, await CountAsync(server, db => db.Accounts.Where(a => a.IsAdmin)));
    }

    [Fact]
    public async Task DeleteRemovesTheAccountAndItsBank()
    {
        using var server = new SyncServerFactory();
        var (ash, ashKey) = await server.RegisterAsync("ash@example.com");
        var (misty, mistyKey) = await server.RegisterAsync("misty@example.com");
        var blob = await misty.UploadAsync(Mons.Bytes(1));
        await misty.PushAsync(Mons.Group(Mons.Write(Guid.NewGuid(), 0, 0, blob)));

        var wrong = await DeleteUserAsync(ash, mistyKey.AccountId, "ash@example.com");
        var self = await DeleteUserAsync(ash, ashKey.AccountId, "ash@example.com");
        var response = await DeleteUserAsync(ash, mistyKey.AccountId, "MISTY@example.com");

        Assert.Equal(HttpStatusCode.BadRequest, wrong.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, self.StatusCode);
        Assert.Equal(ErrorCodes.SelfChange, (await self.ReadErrorAsync()).Code);
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await misty.GetAsync("/v1/devices")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await LoginAsync(server, "misty@example.com", TestApi.Password)).StatusCode);
        Assert.False(Directory.Exists(Path.Combine(server.DataDirectory, "blobs", mistyKey.AccountId.ToString("N"))));
        Assert.Equal(0, await CountAsync(server, db => db.Items.Where(i => i.AccountId == mistyKey.AccountId)));
        Assert.Equal(0, await CountAsync(server, db => db.Blobs.Where(b => b.AccountId == mistyKey.AccountId)));
        Assert.Equal(HttpStatusCode.NotFound, (await DeleteUserAsync(ash, mistyKey.AccountId, "misty@example.com")).StatusCode);
    }

    [Fact]
    public async Task SignUpsSwitchOverridesTheEnvironmentAndSurvivesARestart()
    {
        using var server = new SyncServerFactory();
        server.Settings["Sync:AllowRegistration"] = "false";
        var (ash, _) = await server.RegisterAsync("ash@example.com"); // the first account always can

        Assert.False((await InfoAsync(server)).RegistrationOpen);
        Assert.False((await (await ash.GetAsync("/v1/admin/settings")).ReadAsync<AdminSettings>()).AllowRegistration);
        var closed = await server.Client().PostJsonAsync("/v1/accounts", new RegisterRequest("misty@example.com", TestApi.Password, "Phone"));
        Assert.Equal(HttpStatusCode.Forbidden, closed.StatusCode);
        Assert.Equal(ErrorCodes.RegistrationClosed, (await closed.ReadErrorAsync()).Code);

        var set = await PutJsonAsync(ash, "/v1/admin/settings", new AdminSettings(true));
        Assert.True((await set.ReadAsync<AdminSettings>()).AllowRegistration);
        Assert.True((await InfoAsync(server)).RegistrationOpen);
        await server.RegisterAsync("misty@example.com");
        using (var scope = server.Services.CreateScope())
            Assert.True(await scope.ServiceProvider.GetRequiredService<ServerSettingsStore>().AllowRegistrationAsync(CancellationToken.None));

        // A restart with the same data and ALLOW_REGISTRATION=false keeps the admin's choice.
        using var restarted = new SyncServerFactory();
        restarted.Settings["Sync:DataDirectory"] = server.DataDirectory;
        restarted.Settings["Sync:AllowRegistration"] = "false";
        Assert.True((await InfoAsync(restarted)).RegistrationOpen);
        await restarted.RegisterAsync("brock@example.com");
    }

    [Fact]
    public async Task ClosingSignUpsOverridesAnOpenEnvironment()
    {
        using var server = new SyncServerFactory(); // AllowRegistration=true
        var (ash, _) = await server.RegisterAsync("ash@example.com");

        await PutJsonAsync(ash, "/v1/admin/settings", new AdminSettings(false));

        Assert.False((await InfoAsync(server)).RegistrationOpen);
        var api = await server.Client().PostJsonAsync("/v1/accounts", new RegisterRequest("misty@example.com", TestApi.Password, "Phone"));
        var web = await server.Client().PostJsonAsync("/v1/web/register", new RegisterRequest("misty@example.com", TestApi.Password, "Firefox"));
        Assert.Equal(ErrorCodes.RegistrationClosed, (await api.ReadErrorAsync()).Code);
        Assert.Equal(ErrorCodes.RegistrationClosed, (await web.ReadErrorAsync()).Code);
        // Admins still add accounts.
        Assert.Equal(HttpStatusCode.OK, (await ash.PostJsonAsync("/v1/admin/users", new AdminCreateUserRequest("misty@example.com", false))).StatusCode);
    }

    private static async Task<int> CountAsync<T>(SyncServerFactory server, Func<SyncDbContext, IQueryable<T>> query)
    {
        using var scope = server.Services.CreateScope();
        return await query(scope.ServiceProvider.GetRequiredService<SyncDbContext>()).CountAsync();
    }
}
