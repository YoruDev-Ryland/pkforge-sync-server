using System.Net;
using System.Text.Json;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using PKForge.Sync.Protocol;
using PKForge.Sync.Server.Tests.Infrastructure;
using PKForge.Sync.Server.Web.Details;

namespace PKForge.Sync.Server.Tests.Web;

public class DetailsTests
{
    [Fact]
    public void ReadsAGen3PokemonLikeTheGamesSummary()
    {
        var d = MonDetailsReader.Read(PkFixtures.Sceptile3());
        Assert.True(d.Available);
        Assert.Equal("pk3", d.Format);
        Assert.Equal(3, d.Generation);
        Assert.Equal("Sceptile", d.SpeciesName);
        Assert.Equal("LEAFY", d.Nickname);
        Assert.Equal(62, d.Level);
        Assert.Equal(["Grass"], d.Types);
        Assert.Equal("Leftovers", d.HeldItem);
        Assert.Equal("Luxury Ball", d.Ball);
        Assert.Equal("ASH", d.Ot!.Name);
        Assert.Equal("04817", d.Ot.Tid);
        Assert.Equal("22458", d.Ot.Sid);
        Assert.Equal("Emerald", d.Game);
        Assert.Equal([31, 31, 28, 12, 30, 31], d.Ivs);
        Assert.Equal([4, 252, 0, 0, 0, 252], d.Evs);
        Assert.Equal([70, 85, 65, 105, 85, 120], d.BaseStats);
        Assert.Equal(6, d.Stats.Count);
        Assert.Equal(255, d.Friendship);
        Assert.Equal(234, d.Contest!.Cool);
        Assert.Equal(106, d.Contest.Sheen);
        Assert.Contains(d.Ribbons, r => r.Contains("Champion"));
        Assert.NotNull(d.HiddenPower);
        var leafBlade = d.Moves.First();
        Assert.Equal("Leaf Blade", leafBlade.Name);
        Assert.Equal("Grass", leafBlade.Type);
        Assert.Equal("special", leafBlade.Category); // Gen 3: Grass moves were special
        Assert.Equal(90, leafBlade.Power);
        Assert.Equal(15, leafBlade.MaxPp);
        Assert.Null(d.Met!.Date); // Gen 3 keeps no met date
    }

    [Fact]
    public void Gen4KeepsMetDatesAndUsesTheSplitCategories()
    {
        var d = MonDetailsReader.Read(PkFixtures.Garchomp4());
        Assert.Equal("2026-03-03", d.Met!.Date);
        var dragonRush = d.Moves.First();
        Assert.Equal("physical", dragonRush.Category);
        Assert.Null(d.Contest?.Cool > 0 ? "x" : null);
    }

    [Fact]
    public void LaterFormatsFillTheirExtras()
    {
        var d = MonDetailsReader.Read(PkFixtures.Pikachu9());
        Assert.Equal("Electric", d.Extra.TeraType);
        Assert.Null(d.HiddenPower); // no Hidden Power after Gen 7
        Assert.Null(d.Contest);
    }

    [Fact]
    public void MintedNatureDrivesTheStatArrows()
    {
        var d = MonDetailsReader.Read(PkFixtures.MintedGarchomp9());
        Assert.Equal("Adamant", d.Nature);
        Assert.Equal("Jolly", d.StatNature);
        Assert.Equal(5, d.NatureUp);   // Jolly: +Spe
        Assert.Equal(3, d.NatureDown); // Jolly: -SpA
    }

    [Fact]
    public void Gen1AndGen2MapTheirRawTypeBytesAndHaveNoModernFields()
    {
        var c = MonDetailsReader.Read(PkFixtures.Charmander1());
        Assert.True(c.Available);
        Assert.Equal(["Fire"], c.Types);
        Assert.Equal(12, c.Level);
        Assert.Null(c.Ball);
        Assert.Null(c.Nature);
        Assert.Null(c.Ability);
        Assert.Equal(15, c.IvMax);
        var b = MonDetailsReader.Read(PkFixtures.Bulbasaur2());
        Assert.True(b.Available);
        Assert.Equal(["Grass", "Poison"], b.Types);
        Assert.Equal(14, b.Level);
        Assert.Null(b.Nature);
        Assert.Null(b.Ability);
    }

    [Fact]
    public void CorruptedFilesNeverThrow()
    {
        var bytes = PkFixtures.Pikachu9();
        var probe = PKHeX.Core.EntityFormat.GetFromBytes(bytes)!;
        Assert.NotNull(probe);
        foreach (var offset in Enumerable.Range(0, bytes.Length))
        {
            var copy = bytes.ToArray();
            copy[offset] = 0xFF;
            var d = MonDetailsReader.Read(copy); // must not throw
            Assert.NotNull(d);
        }
        // Nature byte out of range
        var odd = bytes.ToArray();
        odd[0x20] = 0xFF;
        Assert.NotNull(MonDetailsReader.Read(odd));

        var random = new byte[bytes.Length];
        new Random(7).NextBytes(random);
        random[8] = 25; random[9] = 0; // a species
        Assert.NotNull(MonDetailsReader.Read(random));
    }

    [Fact]
    public void IllegalPokemonListWhyInTheNotes()
    {
        var d = MonDetailsReader.Read(PkFixtures.Sceptile3());
        Assert.False(d.Legal);
        Assert.NotEmpty(d.LegalityNotes);
        Assert.True(d.LegalityNotes.Count <= 10);
    }

    [Fact]
    public void GarbageIsUnavailableNotAnError()
    {
        Assert.False(MonDetailsReader.Read(new byte[17]).Available);
        Assert.False(MonDetailsReader.Read(Mons.Bytes(3)).Available);
    }

    [Fact]
    public async Task TheDetailsCacheLimitDoesNotLeakIntoTheSharedMemoryCache()
    {
        using var server = new SyncServerFactory();
        var (client, _) = await server.RegisterAsync();
        var hash = await client.UploadAsync(PkFixtures.Sceptile3());
        var id = Guid.NewGuid();
        await client.PushAsync(Mons.Group(Mons.Write(id, 0, 0, hash)));
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync($"/v1/web/mons/{id}/details")).StatusCode);

        // Any other feature can still cache without giving entries a size.
        var shared = server.Services.GetRequiredService<IMemoryCache>();
        shared.Set("unsized", 1);
        Assert.Equal(1, shared.Get<int>("unsized"));
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync($"/v1/web/mons/{id}/details")).StatusCode);
    }

    [Fact]
    public async Task EndpointReadsTheAccountsOwnPokemon()
    {
        using var server = new SyncServerFactory();
        var (client, _) = await server.RegisterAsync();
        var hash = await client.UploadAsync(PkFixtures.Sceptile3());
        var id = Guid.NewGuid();
        await client.PushAsync(Mons.Group(Mons.Write(id, 0, 0, hash)));

        var response = await client.GetAsync($"/v1/web/mons/{id}/details");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
        Assert.Equal("Sceptile", json.GetProperty("speciesName").GetString());
        Assert.True(json.GetProperty("available").GetBoolean());

        var (other, _) = await server.RegisterAsync("misty@example.com");
        Assert.Equal(HttpStatusCode.NotFound, (await other.GetAsync($"/v1/web/mons/{id}/details")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/v1/web/mons/{Guid.NewGuid()}/details")).StatusCode);
    }
}
