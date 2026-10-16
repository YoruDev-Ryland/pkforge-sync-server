using System.Net;
using System.Text.Json;
using PKForge.Sync.Server.Tests.Infrastructure;

namespace PKForge.Sync.Server.Tests.Web;

public class NamesTests
{
    [Fact]
    public async Task ServesEnglishNamesWithAnETag()
    {
        using var server = new SyncServerFactory();
        var (client, _) = await server.RegisterAsync();
        var response = await client.GetAsync("/v1/web/names");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
        Assert.Equal("Sceptile", json.GetProperty("species")[254].GetString());
        Assert.Equal("Leaf Blade", json.GetProperty("moves")[348].GetString());
        Assert.Equal("Adamant", json.GetProperty("natures")[3].GetString());
        var etag = response.Headers.ETag!;
        var again = new HttpRequestMessage(HttpMethod.Get, "/v1/web/names");
        again.Headers.IfNoneMatch.Add(etag);
        Assert.Equal(HttpStatusCode.NotModified, (await client.SendAsync(again)).StatusCode);
    }
}
