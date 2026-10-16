using PKHeX.Core;

namespace PKForge.Sync.Server.Tests.Infrastructure;

/// <summary>Real Pokémon files built with PKHeX.Core, for tests that parse them.</summary>
public static class PkFixtures
{
    public static byte[] Sceptile3()
    {
        var pk = new PK3
        {
            Species = (ushort)PKHeX.Core.Species.Sceptile, Nickname = "LEAFY", IsNicknamed = true,
            OriginalTrainerName = "ASH", TID16 = 4817, SID16 = 22458, Version = GameVersion.E, Language = (int)LanguageID.English,
            MetLocation = 123, MetLevel = 39, Ball = (byte)PKHeX.Core.Ball.Luxury, HeldItem = 200 /* Gen 3 id of Leftovers; 234 is the Gen 4+ id */, CurrentFriendship = 255,
            PID = 0x957043EC, Move1 = 348, Move2 = 337, Move3 = 89, Move4 = 97,
            IV_HP = 31, IV_ATK = 31, IV_DEF = 28, IV_SPA = 12, IV_SPD = 30, IV_SPE = 31,
            EV_HP = 4, EV_ATK = 252, EV_SPE = 252, ContestCool = 234, ContestTough = 233, ContestSheen = 106,
        };
        pk.EXP = Experience.GetEXP(62, pk.PersonalInfo.EXPGrowth);
        pk.SetMaximumPPCurrent(pk.Moves);
        pk.RibbonChampionG3 = true;
        pk.RefreshChecksum();
        return Stored(pk);
    }

    public static byte[] Garchomp4()
    {
        var pk = new PK4
        {
            Species = (ushort)PKHeX.Core.Species.Garchomp, OriginalTrainerName = "ASH", TID16 = 4817, SID16 = 1,
            Version = GameVersion.Pt, Language = (int)LanguageID.English, MetLocation = 64, MetLevel = 24,
            MetDate = new DateOnly(2026, 3, 3), Ball = (byte)PKHeX.Core.Ball.Poke, PID = 0x12345678,
            Move1 = 407, Move2 = 89, Move3 = 242, Move4 = 424,
        };
        pk.EXP = Experience.GetEXP(55, pk.PersonalInfo.EXPGrowth);
        pk.RefreshChecksum();
        return Stored(pk);
    }

    public static byte[] Pikachu9()
    {
        var pk = new PK9
        {
            Species = (ushort)PKHeX.Core.Species.Pikachu, OriginalTrainerName = "ASH", Version = GameVersion.SL,
            Language = (int)LanguageID.English, MetLevel = 5, Ball = (byte)PKHeX.Core.Ball.Poke, Move1 = 84,
            TeraTypeOriginal = MoveType.Electric, HeightScalar = 128, WeightScalar = 128, Scale = 128,
        };
        pk.EXP = Experience.GetEXP(12, pk.PersonalInfo.EXPGrowth);
        pk.RefreshChecksum();
        return Stored(pk);
    }

    /// <summary>An Adamant Garchomp minted Jolly: Nature and StatAlignment differ (Gen 8+).</summary>
    public static byte[] MintedGarchomp9()
    {
        var pk = new PK9
        {
            Species = (ushort)PKHeX.Core.Species.Garchomp, OriginalTrainerName = "ASH", Version = GameVersion.SL,
            Language = (int)LanguageID.English, MetLevel = 5, Ball = (byte)PKHeX.Core.Ball.Poke, Move1 = 89,
            Nature = Nature.Adamant, StatAlignment = Nature.Jolly, TeraTypeOriginal = MoveType.Dragon,
        };
        pk.EXP = Experience.GetEXP(50, pk.PersonalInfo.EXPGrowth);
        pk.RefreshChecksum();
        return Stored(pk);
    }

    public static byte[] Charmander1()
    {
        var pk = new PK1 { Species = (ushort)PKHeX.Core.Species.Charmander, OriginalTrainerName = "RED", TID16 = 12345, Move1 = 10, Move2 = 52 };
        pk.EXP = Experience.GetEXP(12, pk.PersonalInfo.EXPGrowth);
        pk.SetMaximumPPCurrent(pk.Moves);
        return Stored(pk);
    }

    public static byte[] Bulbasaur2()
    {
        var pk = new PK2 { Species = (ushort)PKHeX.Core.Species.Bulbasaur, OriginalTrainerName = "GOLD", TID16 = 54321, Move1 = 33, Move2 = 73 };
        pk.EXP = Experience.GetEXP(14, pk.PersonalInfo.EXPGrowth);
        pk.SetMaximumPPCurrent(pk.Moves);
        return Stored(pk);
    }

    // The fork has no DecryptedBoxData property; WriteDecryptedDataStored is the equivalent.
    private static byte[] Stored(PKM pk)
    {
        var buffer = new byte[pk.SIZE_STORED];
        pk.WriteDecryptedDataStored(buffer);
        return buffer;
    }
}
