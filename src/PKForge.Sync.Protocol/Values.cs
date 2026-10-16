using System.Text.Json;

namespace PKForge.Sync.Protocol;

/// <summary>A Pokémon in the Bank. <see cref="Info"/> is PKForge's BankEntryInfo, opaque to the server.</summary>
public sealed record MonValue(int Box, int Slot, DateTimeOffset MovedUtc, JsonElement Info, DateTimeOffset AddedUtc, string Blob);

/// <summary>The Bank's box setup. Names and wallpapers are indexed by box; shorter lists mean defaults.</summary>
public sealed record BoxesValue(int Count, IReadOnlyList<string?>? Names, IReadOnlyList<string?>? Wallpapers);

/// <summary>Bank-wide settings that follow the Bank across devices.</summary>
public sealed record SettingsValue(int? LivingDexStartBox);
