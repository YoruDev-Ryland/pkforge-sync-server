using PKHeX.Core;

namespace PKForge.Sync.Server.Web.Details;

/// <summary>Reads a stored Pokémon file with PKHeX.Core into the inspector's <see cref="MonDetails"/>.
/// Every optional section is guarded: a format that can't answer leaves that part empty.</summary>
public static class MonDetailsReader
{
    private static readonly GameStrings Strings = GameInfo.GetStrings("en");
    private static readonly string[] TypeNames = ["Normal", "Fighting", "Flying", "Poison", "Ground", "Rock", "Bug", "Ghost", "Steel",
        "Fire", "Water", "Grass", "Electric", "Psychic", "Ice", "Dragon", "Dark", "Fairy"];
    private static readonly string[] Growth = ["Medium Fast", "Erratic", "Fluctuating", "Medium Slow", "Fast", "Slow"];
    // Stat order HP, Atk, Def, SpA, SpD, Spe. Natures raise (n / 5) and lower (n % 5) of Atk, Def, Spe, SpA, SpD.
    private static readonly int[] NatureStat = [1, 2, 5, 3, 4];

    // Gen 1/2 store raw type bytes: 0-9 as modern (minus the unused 6 "Bird"), then 20-27 for Fire..Dark.
    private static readonly Dictionary<int, string> GbTypes = new()
    {
        [0] = "Normal", [1] = "Fighting", [2] = "Flying", [3] = "Poison", [4] = "Ground", [5] = "Rock", [7] = "Bug", [8] = "Ghost", [9] = "Steel",
        [20] = "Fire", [21] = "Water", [22] = "Grass", [23] = "Electric", [24] = "Psychic", [25] = "Ice", [26] = "Dragon", [27] = "Dark",
    };

    /// <summary>Never throws: anything unreadable is simply "not available".</summary>
    public static MonDetails Read(byte[] data)
    {
        try { return ReadCore(data); }
        catch (Exception) { return new MonDetails { Available = false }; }
    }

    private static string TypeName(int format, int type) =>
        format <= 2 ? GbTypes.GetValueOrDefault(type, "???") : (uint)type < (uint)TypeNames.Length ? TypeNames[type] : "???";

    private static string? NatureName(int nature) => (uint)nature < (uint)Strings.natures.Length ? Strings.natures[nature] : null;

    private static MonDetails ReadCore(byte[] data)
    {
        PKM? pk;
        try { pk = EntityFormat.GetFromBytes(data.ToArray()); }
        catch (Exception) { pk = null; }
        if (pk is null || pk.Species == 0 || pk.Species > pk.MaxSpeciesID) return new MonDetails { Available = false };

        var generation = pk.Format;
        var pi = pk.PersonalInfo;
        Span<int> ivs = stackalloc int[6]; pk.GetIVs(ivs);
        Span<int> evs = stackalloc int[6]; pk.GetEVs(evs);
        var nature = generation >= 3 && (uint)(int)pk.Nature < 25 ? (int)pk.Nature : -1;
        var statNature = generation >= 8 ? (int)pk.StatAlignment : -1;
        // Gen 8+ mints change only the stat nature; the arrows follow whatever the stats use.
        var arrowNature = (uint)statNature < 25 ? statNature : nature;
        var level = pk.CurrentLevel;
        var growth = pi.EXPGrowth;

        var legality = Try(() => new LegalityAnalysis(pk));
        IReadOnlyList<string> notes = [];
        if (legality is { Valid: false })
            notes = Try(() => (IReadOnlyList<string>)legality.Report().Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Take(10).ToList()) ?? [];

        return new MonDetails
        {
            Available = true,
            Format = pk.GetType().Name.ToLowerInvariant(),
            Generation = generation,
            Size = data.Length,
            ChecksumValid = pk.ChecksumValid,
            Pid = generation >= 3 ? pk.PID.ToString("X8") : null,
            EncryptionConstant = generation >= 6 ? pk.EncryptionConstant.ToString("X8") : null,
            Species = pk.Species,
            SpeciesName = Name(Strings.specieslist, pk.Species),
            Form = pk.Form,
            FormName = pk.Form == 0 ? null : Try(() => FormConverter.GetFormList(pk.Species, Strings.types, Strings.forms, GameInfo.GenderSymbolASCII, pk.Context)) is { } forms && pk.Form < forms.Length ? forms[pk.Form] : null,
            Nickname = pk.Nickname,
            IsNicknamed = pk.IsNicknamed,
            IsEgg = pk.IsEgg,
            Gender = pk.Gender switch { 0 => "male", 1 => "female", _ => "genderless" },
            Level = level,
            Exp = pk.EXP,
            ExpThisLevel = growth < Growth.Length ? Experience.GetEXP(level, growth) : 0,
            ExpNextLevel = level >= 100 || growth >= Growth.Length ? null : Experience.GetEXP((byte)(level + 1), growth),
            GrowthRate = growth < Growth.Length ? Growth[growth] : null,
            Types = pi.Type1 == pi.Type2 ? [TypeName(generation, pi.Type1)] : [TypeName(generation, pi.Type1), TypeName(generation, pi.Type2)],
            Nature = nature >= 0 ? NatureName(nature) : null,
            NatureUp = arrowNature >= 0 && arrowNature / 5 != arrowNature % 5 ? NatureStat[arrowNature / 5] : null,
            NatureDown = arrowNature >= 0 && arrowNature / 5 != arrowNature % 5 ? NatureStat[arrowNature % 5] : null,
            StatNature = statNature != nature && (uint)statNature < 25 ? NatureName(statNature) : null,
            Ability = generation >= 3 && pk.Ability > 0 ? Name(Strings.abilitylist, pk.Ability) : null,
            HeldItem = pk.HeldItem > 0 ? Name(Strings.GetItemStrings(pk.Context), pk.HeldItem) : null,
            Ball = pk.Ball > 0 ? Name(Strings.balllist, pk.Ball) : null,
            BallId = pk.Ball,
            Shiny = pk.IsShiny,
            Markings = Markings(pk),
            Pokerus = pk.IsPokerusInfected ? "infected" : pk.IsPokerusCured ? "cured" : null,
            Friendship = pk.CurrentFriendship,
            Ot = new TrainerDetails(pk.OriginalTrainerName, pk.OriginalTrainerGender == 1 ? "female" : "male",
                generation >= 7 ? pk.DisplayTID.ToString("000000") : pk.TID16.ToString("00000"),
                generation >= 3 ? (generation >= 7 ? pk.DisplaySID.ToString("0000") : pk.SID16.ToString("00000")) : null),
            Met = Met(pk),
            Game = Try(() => GameInfo.GetVersionName(pk.Version)),
            Language = Try(() => ((LanguageID)pk.Language).ToString()),
            BaseStats = [pi.HP, pi.ATK, pi.DEF, pi.SPA, pi.SPD, pi.SPE],
            Ivs = StatOrder(ivs),
            Evs = StatOrder(evs),
            Stats = StatOrder(pk.GetStats(pi).Select(s => (int)s).ToArray()),
            IvMax = generation <= 2 ? 15 : 31,
            HiddenPower = generation is >= 2 and <= 7 ? new HiddenPowerDetails(TypeName(3, pk.HPType + 1), pk.HPPower) : null,
            Moves = Moves(pk, generation),
            Contest = pk is IContestStatsReadOnly c && (generation is >= 3 and <= 4 || (c.ContestCool | c.ContestBeauty | c.ContestCute | c.ContestSmart | c.ContestTough) > 0)
                ? Contest(c) : null,
            Ribbons = Try(() => RibbonInfo.GetRibbonInfo(pk).Where(r => r.HasRibbon || r.RibbonCount > 0).Select(r => RibbonName(r.Name)).ToList()) ?? [],
            Legal = legality?.Valid,
            LegalityNotes = notes,
            Extra = new ExtraDetails(
                pk is ITeraType tera ? tera.TeraType.ToString() : null,
                pk is IDynamaxLevel dyn && generation == 8 ? dyn.DynamaxLevel : null,
                pk is IScaledSize3 size ? size.Scale : null,
                pk is IAlpha alpha ? alpha.IsAlpha : null),
        };
    }

    /// <summary>PKHeX orders stats HP, Atk, Def, Spe, SpA, SpD; the inspector wants HP, Atk, Def, SpA, SpD, Spe.</summary>
    private static int[] StatOrder(ReadOnlySpan<int> s) => [s[0], s[1], s[2], s[4], s[5], s[3]];

    private static IReadOnlyList<MoveDetails> Moves(PKM pk, int generation)
    {
        var list = new List<MoveDetails>(4);
        for (var i = 0; i < 4; i++)
        {
            var move = pk.GetMove(i);
            if (move == 0) continue;
            var fact = MoveFacts.Get(move);
            var type = MoveInfo.GetType(move, pk.Context);
            var category = MoveFacts.CategoryIn(generation, fact?.Category ?? MoveCategory.Status, type);
            var ups = i switch { 0 => pk.Move1_PPUps, 1 => pk.Move2_PPUps, 2 => pk.Move3_PPUps, _ => pk.Move4_PPUps };
            var pp = i switch { 0 => pk.Move1_PP, 1 => pk.Move2_PP, 2 => pk.Move3_PP, _ => pk.Move4_PP };
            list.Add(new MoveDetails(Name(Strings.movelist, move), TypeName(3, type),
                category.ToString().ToLowerInvariant(), fact?.Power ?? 0, fact?.Accuracy ?? 0,
                pp, pk.GetMovePP(move, ups), ups));
        }
        return list;
    }

    private static MetDetails? Met(PKM pk) => Try(() =>
    {
        var gen = pk.Generation;
        var hatched = pk.WasEgg;
        return new MetDetails(
            GameInfo.GetLocationName(false, pk.MetLocation, pk.Format, gen, pk.Version),
            pk.Format >= 3 ? pk.MetLevel : null,
            pk.MetDate?.ToString("yyyy-MM-dd"),
            hatched,
            hatched && pk.EggLocation > 0 ? GameInfo.GetLocationName(true, pk.EggLocation, pk.Format, gen, pk.Version) : null,
            pk.EggMetDate?.ToString("yyyy-MM-dd"));
    });

    private static ContestDetails Contest(IContestStatsReadOnly c) =>
        new(c.ContestCool, c.ContestBeauty, c.ContestCute, c.ContestSmart, c.ContestTough, c.ContestSheen);

    private static IReadOnlyList<bool> Markings(PKM pk) => pk switch
    {
        IAppliedMarkings<bool> b => [.. Enumerable.Range(0, b.MarkingCount).Select(b.GetMarking)],
        IAppliedMarkings<MarkingColor> c => [.. Enumerable.Range(0, c.MarkingCount).Select(i => c.GetMarking(i) != MarkingColor.None)],
        _ => [],
    };

    /// <summary>"RibbonChampionG3" → "Champion G3".</summary>
    private static string RibbonName(string property)
    {
        var s = property.StartsWith(RibbonInfo.PropertyPrefix, StringComparison.Ordinal) ? property[RibbonInfo.PropertyPrefix.Length..] : property;
        return string.Concat(s.Select((ch, i) => i > 0 && char.IsUpper(ch) && !char.IsUpper(s[i - 1]) ? " " + ch : ch.ToString()));
    }

    private static string Name(IReadOnlyList<string> names, int id) =>
        (uint)id < (uint)names.Count && names[id].Length > 0 ? names[id] : $"#{id}";

    private static T? Try<T>(Func<T> read)
    {
        try { return read(); }
        catch (Exception)
        {
            return default;
        }
    }
}
