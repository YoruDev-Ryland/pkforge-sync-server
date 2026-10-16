namespace PKForge.Sync.Server.Web.Details;

/// <summary>Everything the web inspector shows about one Pokémon. Stat lists are HP, Atk, Def, SpA, SpD, Spe.</summary>
public sealed record MonDetails
{
    public bool Available { get; init; }
    public string? Format { get; init; }
    public int Generation { get; init; }
    public int Size { get; init; }
    public bool ChecksumValid { get; init; }
    public string? Pid { get; init; }
    public string? EncryptionConstant { get; init; }
    public int Species { get; init; }
    public string? SpeciesName { get; init; }
    public int Form { get; init; }
    public string? FormName { get; init; }
    public string? Nickname { get; init; }
    public bool IsNicknamed { get; init; }
    public bool IsEgg { get; init; }
    public string Gender { get; init; } = "genderless";
    public int Level { get; init; }
    public long Exp { get; init; }
    public long ExpThisLevel { get; init; }
    public long? ExpNextLevel { get; init; }
    public string? GrowthRate { get; init; }
    public IReadOnlyList<string> Types { get; init; } = [];
    public string? Nature { get; init; }
    public int? NatureUp { get; init; }
    public int? NatureDown { get; init; }
    public string? StatNature { get; init; }
    public string? Ability { get; init; }
    public string? HeldItem { get; init; }
    public string? Ball { get; init; }
    public int BallId { get; init; }
    public bool Shiny { get; init; }
    public IReadOnlyList<bool> Markings { get; init; } = [];
    public string? Pokerus { get; init; }
    public int Friendship { get; init; }
    public TrainerDetails? Ot { get; init; }
    public MetDetails? Met { get; init; }
    public string? Game { get; init; }
    public string? Language { get; init; }
    public IReadOnlyList<int> BaseStats { get; init; } = [];
    public IReadOnlyList<int> Ivs { get; init; } = [];
    public IReadOnlyList<int> Evs { get; init; } = [];
    public IReadOnlyList<int> Stats { get; init; } = [];
    public int IvMax { get; init; } = 31;
    public HiddenPowerDetails? HiddenPower { get; init; }
    public IReadOnlyList<MoveDetails> Moves { get; init; } = [];
    public ContestDetails? Contest { get; init; }
    public IReadOnlyList<string> Ribbons { get; init; } = [];
    public bool? Legal { get; init; }
    public IReadOnlyList<string> LegalityNotes { get; init; } = [];
    public ExtraDetails Extra { get; init; } = new();
}

public sealed record TrainerDetails(string Name, string Gender, string Tid, string? Sid);
public sealed record MetDetails(string? Location, int? Level, string? Date, bool Hatched, string? EggLocation, string? EggDate);
public sealed record HiddenPowerDetails(string Type, int Power);
public sealed record MoveDetails(string Name, string Type, string Category, int Power, int Accuracy, int Pp, int MaxPp, int PpUps);
public sealed record ContestDetails(int Cool, int Beauty, int Cute, int Smart, int Tough, int Sheen);
public sealed record ExtraDetails(string? TeraType = null, int? DynamaxLevel = null, int? Scale = null, bool? Alpha = null);
