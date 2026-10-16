using System.Text.Json;
using PKForge.Sync.Protocol;
using PKForge.Sync.Server.Tests.Infrastructure;

namespace PKForge.Sync.Server.Tests.Web;

public class BoxLayoutValidationTests
{
    private static ItemWrite Layout(IReadOnlyList<string?> names, IReadOnlyList<string?> wallpapers) =>
        new(ItemKinds.Boxes, ItemKinds.SingletonId, 0, JsonSerializer.SerializeToElement(new BoxesValue(3, names, wallpapers), ProtocolJson.Options));

    [Theory]
    [InlineData("Shiny hunts in progress!", true)]   // 24 characters
    [InlineData("Shiny hunts in progress!!", false)] // 25
    [InlineData("", true)]
    public async Task NamesAreAtMost24Characters(string name, bool accepted)
    {
        using var server = new SyncServerFactory();
        var (client, _) = await server.RegisterAsync();
        var result = (await client.PushAsync(Mons.Group(Layout([name], [])))).Results.Single();
        Assert.Equal(accepted ? GroupStatus.Accepted : GroupStatus.Rejected, result.Status);
        if (!accepted) Assert.Equal(ErrorCodes.InvalidItem, result.Error);
    }

    [Theory]
    [InlineData("color:3", true)]
    [InlineData("color:999", true)]
    [InlineData("art:box_wp01xy", true)]
    [InlineData("art:box_wp20sv_n", true)]
    [InlineData(null, true)]
    [InlineData("color:1000", false)]
    [InlineData("color:-1", false)]
    [InlineData("art:../etc/passwd", false)]
    [InlineData("art:", false)]
    [InlineData("forest", false)]
    public async Task WallpaperIdsAreColorsOrArtNames(string? id, bool accepted)
    {
        using var server = new SyncServerFactory();
        var (client, _) = await server.RegisterAsync();
        var result = (await client.PushAsync(Mons.Group(Layout([], [id])))).Results.Single();
        Assert.Equal(accepted ? GroupStatus.Accepted : GroupStatus.Rejected, result.Status);
    }
}
