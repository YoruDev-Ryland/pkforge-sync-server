using System.Net;
using PKForge.Sync.Server.Tests.Infrastructure;

namespace PKForge.Sync.Server.Tests.Web;

public class WebHostingTests
{
    private static SyncServerFactory ServerWithWebRoot(out string root)
    {
        var server = new SyncServerFactory();
        root = Path.Combine(server.DataDirectory, "web");
        Directory.CreateDirectory(Path.Combine(root, "assets"));
        File.WriteAllText(Path.Combine(root, "index.html"), "<!doctype html><title>PKForge Bank</title>");
        File.WriteAllText(Path.Combine(root, "assets", "app-abc123.js"), "console.log(1)");
        server.Settings["Sync:WebRoot"] = root;
        return server;
    }

    [Theory]
    [InlineData("/")]
    [InlineData("/bank")]
    [InlineData("/bank/3")]
    [InlineData("/account")]
    public async Task AppRoutesServeTheShell(string path)
    {
        using var server = ServerWithWebRoot(out _);
        var response = await server.CreateClient().GetAsync(path);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("PKForge Bank", await response.Content.ReadAsStringAsync());
        Assert.Equal("no-cache", response.Headers.CacheControl?.ToString());
    }

    [Fact]
    public async Task HashedAssetsAreCachedForever()
    {
        using var server = ServerWithWebRoot(out _);
        var response = await server.CreateClient().GetAsync("/assets/app-abc123.js");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("immutable", response.Headers.CacheControl!.ToString());
    }

    [Theory]
    [InlineData("/v1/nope")]
    [InlineData("/assets/missing.js")]
    [InlineData("/pkhex/sprites/b_99999.png")]
    public async Task ApiAndAssetMissesAreRealNotFounds(string path)
    {
        using var server = ServerWithWebRoot(out _);
        var client = server.Client();
        var response = await client.GetAsync(path);
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task ExistingServerPagesStillWin()
    {
        using var server = ServerWithWebRoot(out _);
        var page = await server.CreateClient().GetStringAsync("/reset-password?token=x");
        Assert.DoesNotContain("<title>PKForge Bank</title>", page);
        Assert.Equal("ok", await server.CreateClient().GetStringAsync("/healthz"));
    }

    [Fact]
    public async Task PkhexSpritesAndWallpapersAreServed()
    {
        using var server = new SyncServerFactory();
        var client = server.CreateClient();
        var sprite = await client.GetAsync("/pkhex/sprites/b_254.png");
        Assert.Equal(HttpStatusCode.OK, sprite.StatusCode);
        Assert.Equal("image/png", sprite.Content.Headers.ContentType?.MediaType);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/pkhex/sprites/b_254s.png")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/pkhex/wallpapers/box_wp01e.png")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/pkhex/balls/_ball11.png")).StatusCode);
    }

    [Fact]
    public async Task WithoutABuiltWebAppTheRootSaysSo()
    {
        using var server = new SyncServerFactory();
        server.Settings["Sync:WebRoot"] = Path.Combine(server.DataDirectory, "no-web");
        var response = await server.CreateClient().GetAsync("/");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}
