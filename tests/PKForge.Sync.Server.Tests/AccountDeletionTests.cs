using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PKForge.Sync.Protocol;
using PKForge.Sync.Server.Data;
using PKForge.Sync.Server.Tests.Infrastructure;

namespace PKForge.Sync.Server.Tests;

public sealed class AccountDeletionTests
{
    [Fact]
    public async Task DeleteAccountNeedsThePassword()
    {
        using var server = new SyncServerFactory();
        var (client, _) = await server.RegisterAsync();

        var response = await client.SendAsync(new HttpRequestMessage(HttpMethod.Delete, "/v1/account")
        {
            Content = JsonContent.Create(new DeleteAccountRequest("wrong-password"), options: ProtocolJson.Options),
        });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/v1/devices")).StatusCode);
    }

    [Fact]
    public async Task DeleteAccountRemovesOnlyThatAccount()
    {
        using var server = new SyncServerFactory();
        var (ash, ashKey) = await server.RegisterAsync("ash@example.com");
        var (misty, _) = await server.RegisterAsync("misty@example.com");
        var ashBlob = await ash.UploadAsync(Mons.Bytes(1));
        var mistyBlob = await misty.UploadAsync(Mons.Bytes(1));
        await ash.PushAsync(Mons.Group(Mons.Write(Guid.NewGuid(), 0, 0, ashBlob)));
        await misty.PushAsync(Mons.Group(Mons.Write(Guid.NewGuid(), 0, 0, mistyBlob)));
        await ash.PutAsJsonAsync("/v1/account/preferences", new { theme = "fire" });
        await misty.PutAsJsonAsync("/v1/account/preferences", new { theme = "water" });

        var response = await ash.SendAsync(new HttpRequestMessage(HttpMethod.Delete, "/v1/account")
        {
            Content = JsonContent.Create(new DeleteAccountRequest(TestApi.Password), options: ProtocolJson.Options),
        });

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await ash.GetAsync("/v1/devices")).StatusCode);
        Assert.False(Directory.Exists(Path.Combine(server.DataDirectory, "blobs", ashKey.AccountId.ToString("N"))));
        Assert.Single((await misty.ChangesAsync(0)).Items);
        using (var scope = server.Services.CreateScope())
        {
            var prefs = await scope.ServiceProvider.GetRequiredService<SyncDbContext>().Preferences.AsNoTracking().ToListAsync();
            Assert.DoesNotContain(prefs, p => p.AccountId == ashKey.AccountId);
            Assert.Single(prefs);
        }
        Assert.Equal(HttpStatusCode.OK, (await misty.GetAsync($"/v1/blobs/{mistyBlob}")).StatusCode);
        var relogin = await server.Client().PostJsonAsync("/v1/auth/login", new LoginRequest("ash@example.com", TestApi.Password, "Phone"));
        Assert.Equal(HttpStatusCode.Unauthorized, relogin.StatusCode);
    }
}
