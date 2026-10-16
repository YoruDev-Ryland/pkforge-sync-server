using PKForge.Sync.Server.Tests.Infrastructure;
using PKHeX.Core;

namespace PKForge.Sync.Server.Tests.Web;

public class PkhexSetupTests
{
    [Fact]
    public void FixturesParseBackWithPkhex()
    {
        var pk = EntityFormat.GetFromBytes(PkFixtures.Sceptile3());
        Assert.NotNull(pk);
        Assert.Equal((ushort)Species.Sceptile, pk!.Species);
        Assert.Equal("LEAFY", pk.Nickname);
        Assert.Equal(62, pk.CurrentLevel);
        Assert.Equal((ushort)Species.Garchomp, EntityFormat.GetFromBytes(PkFixtures.Garchomp4())!.Species);
        Assert.Equal((ushort)Species.Pikachu, EntityFormat.GetFromBytes(PkFixtures.Pikachu9())!.Species);
    }
}
