using System.IO.Compression;
using System.Text;

namespace PKForge.Sync.Server.Web.Details;

public enum MoveCategory : byte { Status, Physical, Special }
public sealed record MoveFact(int Id, int Type, MoveCategory Category, int Power, int Accuracy);

/// <summary>Move power, accuracy and (modern) category from the app's dexfacts table ("M" rows).</summary>
public static class MoveFacts
{
    private static readonly Lazy<Dictionary<int, MoveFact>> Table = new(Load);

    public static MoveFact? Get(int move) => Table.Value.GetValueOrDefault(move);

    /// <summary>Gen 1-3: Fire, Water, Grass, Electric, Psychic, Ice, Dragon and Dark (types 9-16) were special.</summary>
    public static MoveCategory CategoryIn(int generation, MoveCategory modern, int typeInGame) =>
        generation >= 4 || modern == MoveCategory.Status ? modern
        : typeInGame is >= 9 and <= 16 ? MoveCategory.Special : MoveCategory.Physical;

    private static Dictionary<int, MoveFact> Load()
    {
        using var stream = typeof(MoveFacts).Assembly.GetManifestResourceStream("PKForge.Sync.Server.dexfacts.tsv.gz")
            ?? throw new InvalidOperationException("dexfacts.tsv.gz is not embedded");
        using var inflate = new GZipStream(stream, CompressionMode.Decompress);
        using var reader = new StreamReader(inflate, Encoding.UTF8);
        var moves = new Dictionary<int, MoveFact>();
        while (reader.ReadLine() is { } line)
        {
            var f = line.Split('\t');
            if (f.Length >= 6 && f[0] == "M" && int.TryParse(f[1], out var id) && int.TryParse(f[2], out var type)
                && int.TryParse(f[3], out var cat) && int.TryParse(f[4], out var power) && int.TryParse(f[5], out var acc))
                moves[id] = new MoveFact(id, type, (MoveCategory)Math.Clamp(cat, 0, 2), power, acc);
        }
        return moves;
    }
}
