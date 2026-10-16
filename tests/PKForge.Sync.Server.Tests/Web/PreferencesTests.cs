using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using PKForge.Sync.Server.Tests.Infrastructure;

namespace PKForge.Sync.Server.Tests.Web;

public class PreferencesTests
{
    [Fact]
    public async Task EmptyByDefaultThenRoundTripsKeepingUnknownKeys()
    {
        using var server = new SyncServerFactory();
        var (client, _) = await server.RegisterAsync();
        Assert.Equal("{}", (await client.GetStringAsync("/v1/account/preferences")).Trim());
        var put = await client.PutAsJsonAsync("/v1/account/preferences", new { mode = "dark", theme = "fire", future = new { x = 1 } });
        Assert.Equal(HttpStatusCode.NoContent, put.StatusCode);
        var back = JsonDocument.Parse(await client.GetStringAsync("/v1/account/preferences")).RootElement;
        Assert.Equal("dark", back.GetProperty("mode").GetString());
        Assert.Equal(1, back.GetProperty("future").GetProperty("x").GetInt32());
    }

    [Fact]
    public async Task PreferencesArePerAccount()
    {
        using var server = new SyncServerFactory();
        var (ash, _) = await server.RegisterAsync();
        var (misty, _) = await server.RegisterAsync("misty@example.com");
        await ash.PutAsJsonAsync("/v1/account/preferences", new { theme = "fire" });
        Assert.Equal("{}", (await misty.GetStringAsync("/v1/account/preferences")).Trim());
    }

    [Fact]
    public async Task RejectsNonObjectsAndOversizedBodies()
    {
        using var server = new SyncServerFactory();
        var (client, _) = await server.RegisterAsync();
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PutAsJsonAsync("/v1/account/preferences", new[] { 1, 2 })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PutAsJsonAsync("/v1/account/preferences", new { pad = new string('x', 5000) })).StatusCode);
    }

    [Theory]
    [InlineData("{")]
    [InlineData("")]
    public async Task RejectsMalformedAndEmptyBodies(string body)
    {
        using var server = new SyncServerFactory();
        var (client, _) = await server.RegisterAsync();
        var response = await client.PutAsync("/v1/account/preferences", new StringContent(body, System.Text.Encoding.UTF8, "application/json"));
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task AcceptsExactlyTheCapAndRejectsOneByteMore()
    {
        using var server = new SyncServerFactory();
        var (client, _) = await server.RegisterAsync();
        static StringContent Body(int bytes) =>
            new("{\"p\":\"" + new string('x', bytes - 8) + "\"}", System.Text.Encoding.UTF8, "application/json");
        Assert.Equal(HttpStatusCode.NoContent, (await client.PutAsync("/v1/account/preferences", Body(4096))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PutAsync("/v1/account/preferences", Body(4097))).StatusCode);
    }

    [Fact]
    public async Task NeedsSignIn()
    {
        using var server = new SyncServerFactory();
        Assert.Equal(HttpStatusCode.Unauthorized, (await server.Client().GetAsync("/v1/account/preferences")).StatusCode);
    }
}
