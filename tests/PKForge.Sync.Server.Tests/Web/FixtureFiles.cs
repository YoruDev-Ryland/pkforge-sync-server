using PKForge.Sync.Server.Tests.Infrastructure;

namespace PKForge.Sync.Server.Tests.Web;

/// <summary>The web's end-to-end tests upload real Pokémon files. They are the <see cref="PkFixtures"/> bytes, written to
/// web/tests/e2e/fixtures when PKF_WRITE_E2E_FIXTURES=1 and checked to be current otherwise.</summary>
public class FixtureFiles
{
    public static TheoryData<string> Names => [.. Files.Keys];

    private static readonly Dictionary<string, Func<byte[]>> Files = new()
    {
        ["charmander.pk1"] = PkFixtures.Charmander1,
        ["bulbasaur.pk2"] = PkFixtures.Bulbasaur2,
        ["sceptile.pk3"] = PkFixtures.Sceptile3,
        ["garchomp.pk4"] = PkFixtures.Garchomp4,
        ["pikachu.pk9"] = PkFixtures.Pikachu9,
    };

    [Theory]
    [MemberData(nameof(Names))]
    public void E2eFixtureIsCurrent(string name)
    {
        var path = Path.Combine(FixtureDir(), name);
        var bytes = Files[name]();
        if (Environment.GetEnvironmentVariable("PKF_WRITE_E2E_FIXTURES") == "1")
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllBytes(path, bytes);
        }
        Assert.True(File.Exists(path), $"{path} is missing; run the tests once with PKF_WRITE_E2E_FIXTURES=1.");
        Assert.Equal(bytes, File.ReadAllBytes(path));
    }

    /// <summary>web/tests/e2e/fixtures in this repo, found by walking up from the test binaries.</summary>
    private static string FixtureDir()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
            if (File.Exists(Path.Combine(dir.FullName, "web", "package.json")))
                return Path.Combine(dir.FullName, "web", "tests", "e2e", "fixtures");
        throw new DirectoryNotFoundException("No web/package.json above " + AppContext.BaseDirectory);
    }
}
