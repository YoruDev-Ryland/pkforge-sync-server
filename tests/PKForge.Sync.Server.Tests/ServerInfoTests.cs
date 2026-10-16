using System.Net;
using PKForge.Sync.Protocol;
using PKForge.Sync.Server.Tests.Infrastructure;

namespace PKForge.Sync.Server.Tests;

public sealed class ServerInfoTests
{
    [Fact]
    public async Task ServerInfoNeedsNoProtocolHeader()
    {
        using var server = new SyncServerFactory();
        var response = await server.CreateClient().GetAsync("/v1/server-info");

        response.EnsureSuccessStatusCode();
        var info = await response.ReadAsync<ServerInfo>();
        Assert.Equal(SyncProtocol.MinSupportedVersion, info.MinProtocol);
        Assert.Equal(SyncProtocol.Version, info.MaxProtocol);
        Assert.Equal("PKForge Sync", info.Name);
    }

    [Theory]
    [InlineData("  Misty's Bank  ", "Misty's Bank")]
    [InlineData("   ", "PKForge Sync")]
    public async Task ServerNameIsTrimmedAndBlankMeansTheDefault(string configured, string shown)
    {
        using var server = new SyncServerFactory();
        server.Settings["Sync:ServerName"] = configured;

        var info = await (await server.CreateClient().GetAsync("/v1/server-info")).ReadAsync<ServerInfo>();

        Assert.Equal(shown, info.Name);
    }

    [Fact]
    public async Task ServerNameIsCutToSixtyCharacters()
    {
        using var server = new SyncServerFactory();
        server.Settings["Sync:ServerName"] = new string('x', 80);

        var info = await (await server.CreateClient().GetAsync("/v1/server-info")).ReadAsync<ServerInfo>();

        Assert.Equal(new string('x', 60), info.Name);
    }

    [Fact]
    public async Task ServerInfoJsonHasTheNameAndNoKind()
    {
        using var server = new SyncServerFactory();
        var json = await server.CreateClient().GetStringAsync("/v1/server-info");

        Assert.Contains("\"name\":\"PKForge Sync\"", json);
        Assert.DoesNotContain("serverKind", json);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("0")]
    [InlineData("99")]
    [InlineData("abc")]
    public async Task OtherEndpointsRejectMissingOrUnsupportedProtocol(string? header)
    {
        using var server = new SyncServerFactory();
        var client = server.CreateClient();
        if (header is not null) client.DefaultRequestHeaders.Add(SyncProtocol.VersionHeader, header);

        var response = await client.GetAsync("/v1/anything");

        Assert.Equal(HttpStatusCode.UpgradeRequired, response.StatusCode);
        Assert.Equal(ErrorCodes.UnsupportedProtocol, (await response.ReadErrorAsync()).Code);
    }

    [Fact]
    public async Task SupportedProtocolPassesThrough()
    {
        using var server = new SyncServerFactory();
        var response = await server.Client().GetAsync("/v1/anything");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task HealthCheckAnswers()
    {
        using var server = new SyncServerFactory();
        var response = await server.CreateClient().GetAsync("/healthz");
        response.EnsureSuccessStatusCode();
    }
}
