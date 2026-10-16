using System.Net;
using System.Text.Json;
using PKForge.Sync.Protocol;
using PKForge.Sync.Server.Tests.Infrastructure;

namespace PKForge.Sync.Server.Tests;

public sealed class PushTests
{
    private static readonly Guid A = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000001");
    private static readonly Guid B = Guid.Parse("bbbbbbbb-0000-0000-0000-000000000002");
    private static readonly Guid C = Guid.Parse("cccccccc-0000-0000-0000-000000000003");

    private static async Task<(SyncServerFactory Server, HttpClient Client, string Blob)> StartAsync()
    {
        var server = new SyncServerFactory();
        var (client, _) = await server.RegisterAsync();
        var blob = await client.UploadAsync(Mons.Bytes(1));
        return (server, client, blob);
    }

    private static MonValue Mon(SyncItem item) => item.Value!.Value.Deserialize<MonValue>(ProtocolJson.Options)!;

    [Fact]
    public async Task NewItemsGetIncreasingVersions()
    {
        var (server, client, blob) = await StartAsync();
        using var _ = server;

        var response = await client.PushAsync(Mons.Group(Mons.Boxes(2), Mons.Write(A, 0, 0, blob)));

        var result = Assert.Single(response.Results);
        Assert.Equal(GroupStatus.Accepted, result.Status);
        Assert.Equal([1L, 2L], result.Items.Select(i => i.Version));
        Assert.Equal(2, response.Version);
    }

    [Fact]
    public async Task EditOnTheCurrentVersionIsAccepted()
    {
        var (server, client, blob) = await StartAsync();
        using var _ = server;
        var v1 = (await client.PushAsync(Mons.Group(Mons.Write(A, 0, 0, blob)))).Results[0].Items[0].Version;

        var edit = await client.PushAsync(Mons.Group(Mons.Write(A, 0, 0, blob, baseVersion: v1, nickname: "Sparky")));

        Assert.Equal(GroupStatus.Accepted, edit.Results[0].Status);
        Assert.Equal(v1 + 1, edit.Results[0].Items[0].Version);
    }

    [Fact]
    public async Task EditOnAStaleVersionIsAConflictCarryingTheServerCopy()
    {
        var (server, client, blob) = await StartAsync();
        using var _ = server;
        await client.PushAsync(Mons.Group(Mons.Write(A, 0, 0, blob)));
        await client.PushAsync(Mons.Group(Mons.Write(A, 0, 0, blob, baseVersion: 1, nickname: "Sparky")));

        var stale = await client.PushAsync(Mons.Group(Mons.Write(A, 0, 0, blob, baseVersion: 1, nickname: "Volt")));

        var result = stale.Results[0];
        Assert.Equal(GroupStatus.Conflict, result.Status);
        var item = Assert.Single(result.Items);
        Assert.Equal(2, item.Version);
        Assert.Equal("Sparky", Mon(item).Info.GetProperty("nickname").GetString());
    }

    [Fact]
    public async Task EditingAnItemTheServerNeverHadIsAConflictAtVersionZero()
    {
        var (server, client, blob) = await StartAsync();
        using var _ = server;

        var result = (await client.PushAsync(Mons.Group(Mons.Write(A, 0, 0, blob, baseVersion: 7)))).Results[0];

        Assert.Equal(GroupStatus.Conflict, result.Status);
        Assert.Equal(0, result.Items[0].Version);
        Assert.True(result.Items[0].Deleted);
    }

    [Fact]
    public async Task AConflictAnywhereInAGroupChangesNothing()
    {
        var (server, client, blob) = await StartAsync();
        using var _ = server;
        await client.PushAsync(Mons.Group(Mons.Write(A, 0, 0, blob)));

        var mixed = await client.PushAsync(Mons.Group(Mons.Write(B, 0, 1, blob), Mons.Write(A, 0, 0, blob, baseVersion: 9, nickname: "Volt")));

        Assert.Equal(GroupStatus.Conflict, mixed.Results[0].Status);
        Assert.Equal(1, mixed.Version);
    }

    [Fact]
    public async Task RetriedGroupIsNotAppliedTwice()
    {
        var (server, client, blob) = await StartAsync();
        using var _ = server;
        var group = Mons.Group(Mons.Write(A, 0, 0, blob));

        var first = await client.PushAsync(group);
        var retry = await client.PushAsync(group);

        Assert.Equal(GroupStatus.Accepted, retry.Results[0].Status);
        Assert.Equal(first.Results[0].Items.Select(i => i.Version), retry.Results[0].Items.Select(i => i.Version));
        Assert.Equal(1, retry.Version);
    }

    [Fact]
    public async Task WritingTheValueTheServerAlreadyHasIsANoOp()
    {
        var (server, client, blob) = await StartAsync();
        using var _ = server;
        await client.PushAsync(Mons.Group(Mons.Write(A, 0, 0, blob)));

        var same = await client.PushAsync(Mons.Group(Mons.Write(A, 0, 0, blob, baseVersion: 0)));

        Assert.Equal(GroupStatus.Accepted, same.Results[0].Status);
        Assert.Equal(1, same.Results[0].Items[0].Version);
        Assert.Equal(1, same.Version);
    }

    [Fact]
    public async Task ATakenSlotIsReportedWithItsOccupant()
    {
        var (server, client, blob) = await StartAsync();
        using var _ = server;
        await client.PushAsync(Mons.Group(Mons.Write(A, 0, 0, blob)));

        var result = (await client.PushAsync(Mons.Group(Mons.Write(B, 0, 0, blob)))).Results[0];

        Assert.Equal(GroupStatus.SlotTaken, result.Status);
        Assert.Equal(ErrorCodes.SlotTaken, result.Error);
        Assert.Equal(A.ToString(), Assert.Single(result.Items).Id);
    }

    [Fact]
    public async Task SwapInsideOneGroupIsAccepted()
    {
        var (server, client, blob) = await StartAsync();
        using var _ = server;
        await client.PushAsync(Mons.Group(Mons.Write(A, 0, 0, blob), Mons.Write(B, 0, 1, blob)));

        var swap = await client.PushAsync(Mons.Group(Mons.Write(A, 0, 1, blob, baseVersion: 1), Mons.Write(B, 0, 0, blob, baseVersion: 2)));

        Assert.Equal(GroupStatus.Accepted, swap.Results[0].Status);
        Assert.Equal([1, 0], swap.Results[0].Items.Select(i => Mon(i).Slot));
    }

    [Fact]
    public async Task TwoPokemonCannotTargetOneSlotInAGroup()
    {
        var (server, client, blob) = await StartAsync();
        using var _ = server;

        var result = (await client.PushAsync(Mons.Group(Mons.Write(A, 0, 0, blob), Mons.Write(B, 0, 0, blob)))).Results[0];

        Assert.Equal(GroupStatus.Rejected, result.Status);
        Assert.Equal(ErrorCodes.InvalidItem, result.Error);
    }

    [Fact]
    public async Task ReleasingAPokemonFreesItsSlot()
    {
        var (server, client, blob) = await StartAsync();
        using var _ = server;
        await client.PushAsync(Mons.Group(Mons.Write(A, 0, 0, blob)));

        var release = await client.PushAsync(Mons.Group(Mons.Delete(A, baseVersion: 1)));
        var reuse = await client.PushAsync(Mons.Group(Mons.Write(B, 0, 0, blob)));

        Assert.True(release.Results[0].Items[0].Deleted);
        Assert.Null(release.Results[0].Items[0].Value);
        Assert.Equal(GroupStatus.Accepted, reuse.Results[0].Status);
    }

    [Fact]
    public async Task PokemonMustReferenceUploadedBlobsOfTheirOwnAccount()
    {
        var (server, client, _) = await StartAsync();
        using var __ = server;
        var (misty, _) = await server.RegisterAsync("misty@example.com");
        var mistyBlob = await misty.UploadAsync(Mons.Bytes(42));

        var neverUploaded = (await client.PushAsync(Mons.Group(Mons.Write(A, 0, 0, BlobHash.Compute(Mons.Bytes(99)))))).Results[0];
        var someoneElses = (await client.PushAsync(Mons.Group(Mons.Write(B, 0, 1, mistyBlob)))).Results[0];

        Assert.Equal(ErrorCodes.MissingBlob, neverUploaded.Error);
        Assert.Equal(ErrorCodes.MissingBlob, someoneElses.Error);
    }

    [Fact]
    public async Task PokemonMustSitInsideTheBoxes()
    {
        var (server, client, blob) = await StartAsync();
        using var _ = server;
        await client.PushAsync(Mons.Group(Mons.Boxes(2), Mons.Write(A, 1, 0, blob)));

        var outside = (await client.PushAsync(Mons.Group(Mons.Write(B, 2, 0, blob)))).Results[0];
        var shrink = (await client.PushAsync(Mons.Group(Mons.Boxes(1, baseVersion: 1)))).Results[0];
        var moveThenShrink = (await client.PushAsync(Mons.Group(Mons.Boxes(1, baseVersion: 1), Mons.Write(A, 0, 0, blob, baseVersion: 2)))).Results[0];

        Assert.Equal(ErrorCodes.InvalidItem, outside.Error);
        Assert.Equal(ErrorCodes.InvalidItem, shrink.Error);
        Assert.Equal(GroupStatus.Accepted, moveThenShrink.Status);
    }

    [Fact]
    public async Task ShrinkingTheBoxesNeedsTheRemovedBoxesEmpty()
    {
        var (server, client, blob) = await StartAsync();
        using var _ = server;
        await client.PushAsync(Mons.Group(Mons.Boxes(4), Mons.Write(A, 2, 5, blob))); // boxes v1, A v2

        var withMon = (await client.PushAsync(Mons.Group(Mons.Boxes(2, baseVersion: 1)))).Results[0];
        var emptyOnly = (await client.PushAsync(Mons.Group(Mons.Boxes(3, baseVersion: 1)))).Results[0]; // box 3 is empty
        var moveDownFirst = (await client.PushAsync(Mons.Group(Mons.Write(A, 1, 5, blob, baseVersion: 2), Mons.Boxes(2, baseVersion: 3)))).Results[0];

        Assert.Equal(GroupStatus.Rejected, withMon.Status);
        Assert.Equal(ErrorCodes.InvalidItem, withMon.Error);
        Assert.Equal("Move the Pokémon out of the boxes you're removing first.", withMon.Message);
        Assert.Equal(GroupStatus.Accepted, emptyOnly.Status);
        Assert.Equal(GroupStatus.Accepted, moveDownFirst.Status);
    }

    [Fact]
    public async Task TheFairUseCapCountsLivePokemon()
    {
        using var server = new SyncServerFactory();
        server.Settings["Sync:MaxLiveMonsPerAccount"] = "2";
        var (client, _) = await server.RegisterAsync();
        var blob = await client.UploadAsync(Mons.Bytes(1));
        await client.PushAsync(Mons.Group(Mons.Write(A, 0, 0, blob), Mons.Write(B, 0, 1, blob)));

        var third = (await client.PushAsync(Mons.Group(Mons.Write(C, 0, 2, blob)))).Results[0];
        var swapOut = (await client.PushAsync(Mons.Group(Mons.Delete(A, 1), Mons.Write(C, 0, 2, blob)))).Results[0];

        Assert.Equal(ErrorCodes.QuotaExceeded, third.Error);
        Assert.Equal(GroupStatus.Accepted, swapOut.Status);
    }

    public static TheoryData<ItemWrite> InvalidWrites() => new()
    {
        new ItemWrite(ItemKinds.Mon, "not-a-guid", 0, JsonSerializer.SerializeToElement(new { box = 0 })),
        new ItemWrite("party", "x", 0, JsonSerializer.SerializeToElement(new { })),
        new ItemWrite(ItemKinds.Mon, Guid.NewGuid().ToString(), 0, null),
        new ItemWrite(ItemKinds.Mon, Guid.NewGuid().ToString(), -1, null, Deleted: true),
        new ItemWrite(ItemKinds.Boxes, "other", 0, JsonSerializer.SerializeToElement(new { count = 1 })),
        new ItemWrite(ItemKinds.Boxes, ItemKinds.SingletonId, 0, JsonSerializer.SerializeToElement(new { count = 0 })),
        new ItemWrite(ItemKinds.Settings, ItemKinds.SingletonId, 0, null, Deleted: true),
    };

    [Theory]
    [MemberData(nameof(InvalidWrites))]
    public async Task MalformedWritesAreRejected(ItemWrite write)
    {
        var (server, client, _) = await StartAsync();
        using var __ = server;

        var result = (await client.PushAsync(Mons.Group(write))).Results[0];

        Assert.Equal(GroupStatus.Rejected, result.Status);
        Assert.Equal(ErrorCodes.InvalidItem, result.Error);
    }

    [Theory]
    [InlineData(-1, 0)]
    [InlineData(0, 30)]
    public async Task SlotsOutsideTheBoxShapeAreRejected(int box, int slot)
    {
        var (server, client, blob) = await StartAsync();
        using var _ = server;

        var result = (await client.PushAsync(Mons.Group(Mons.Write(A, box, slot, blob)))).Results[0];

        Assert.Equal(ErrorCodes.InvalidItem, result.Error);
    }

    [Fact]
    public async Task SettingsAreStoredAsGiven()
    {
        var (server, client, _) = await StartAsync();
        using var _ = server;
        var write = new ItemWrite(ItemKinds.Settings, ItemKinds.SingletonId, 0,
            JsonSerializer.SerializeToElement(new SettingsValue(LivingDexStartBox: 10), ProtocolJson.Options));

        var result = (await client.PushAsync(Mons.Group(write))).Results[0];

        Assert.Equal(GroupStatus.Accepted, result.Status);
        Assert.Equal(10, result.Items[0].Value!.Value.GetProperty("livingDexStartBox").GetInt32());
    }

    [Fact]
    public async Task OversizedPushesAreRefused()
    {
        var (server, client, _) = await StartAsync();
        using var _ = server;
        var groups = Enumerable.Range(0, 501).Select(_ => Mons.Group()).ToList();

        var response = await client.PostJsonAsync("/v1/sync/push", new PushRequest(groups));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }
}
