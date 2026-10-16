using System.Text.Json;
using System.Text.Json.Nodes;
using PKForge.Sync.Protocol;
using PKForge.Sync.Server.Data;

namespace PKForge.Sync.Server.Sync;

/// <summary>An <see cref="ItemWrite"/> that passed shape validation, with its value decoded.</summary>
internal sealed class ParsedWrite
{
    public const int MaxBoxes = 10_000;
    public const int MaxBoxNameLength = 24;

    /// <summary>"color:N" (palette index 0-999), "art:NAME" (bundled wallpaper asset), or null for the default.</summary>
    public static bool IsWallpaperId(string? id)
    {
        if (id is null) return true;
        if (id.StartsWith("color:", StringComparison.Ordinal))
            return id.Length is > 6 and <= 9 && id.AsSpan(6).IndexOfAnyExceptInRange('0', '9') < 0;
        if (id.StartsWith("art:", StringComparison.Ordinal))
            return id.Length is > 4 and <= 68 && id.AsSpan(4).ToString().All(c => char.IsAsciiLetterOrDigit(c) || c == '_');
        return false;
    }

    public required string Kind { get; init; }
    public required string Id { get; init; }
    public long BaseVersion { get; init; }
    public bool Deleted { get; init; }
    public string? ValueJson { get; init; }
    /// <summary>Set for live mon writes.</summary>
    public MonValue? Mon { get; init; }
    /// <summary>Set for boxes writes.</summary>
    public BoxesValue? Boxes { get; init; }

    /// <summary>True when storing this write would change nothing.</summary>
    public bool SameAs(Item? item)
    {
        if (item is null || item.Deleted) return Deleted;
        return !Deleted && JsonNode.DeepEquals(JsonNode.Parse(item.Value!), JsonNode.Parse(ValueJson!));
    }

    public static ParsedWrite? TryParse(ItemWrite? write, out string error)
    {
        error = "";
        if (write is null) return Fail("Empty write.", out error);
        if (write.BaseVersion < 0) return Fail("Base versions are 0 or more.", out error);

        switch (write.Kind)
        {
            case ItemKinds.Mon:
                if (!Guid.TryParse(write.Id, out var id)) return Fail($"Pokémon ids are GUIDs, not '{write.Id}'.", out error);
                if (write.Deleted) return new ParsedWrite { Kind = ItemKinds.Mon, Id = id.ToString("D"), BaseVersion = write.BaseVersion, Deleted = true };
                if (Decode<MonValue>(write.Value) is not { } mon) return Fail("A Pokémon needs a value.", out error);
                if (mon.Box < 0 || mon.Box >= MaxBoxes || mon.Slot < 0 || mon.Slot >= SyncProtocol.SlotsPerBox)
                    return Fail($"Box {mon.Box} slot {mon.Slot} is outside the Bank.", out error);
                if (!BlobHash.IsValid(mon.Blob)) return Fail("A Pokémon needs a lowercase SHA-256 blob id.", out error);
                if (mon.Info.ValueKind != JsonValueKind.Object) return Fail("A Pokémon needs an info object.", out error);
                return new ParsedWrite { Kind = ItemKinds.Mon, Id = id.ToString("D"), BaseVersion = write.BaseVersion, ValueJson = write.Value!.Value.GetRawText(), Mon = mon };

            case ItemKinds.Boxes:
                if (write.Id != ItemKinds.SingletonId || write.Deleted) return Fail("There is one boxes item, id 'bank', and it can't be deleted.", out error);
                if (Decode<BoxesValue>(write.Value) is not { } boxes || boxes.Count < 1 || boxes.Count > MaxBoxes
                    || (boxes.Names?.Count ?? 0) > boxes.Count || (boxes.Wallpapers?.Count ?? 0) > boxes.Count)
                    return Fail($"Boxes need a count from 1 to {MaxBoxes} and at most that many names and wallpapers.", out error);
                if (boxes.Names?.Any(n => n is { Length: > MaxBoxNameLength }) == true)
                    return Fail($"Box names are at most {MaxBoxNameLength} characters.", out error);
                if (boxes.Wallpapers?.All(IsWallpaperId) == false)
                    return Fail("Wallpapers are \"color:N\" or \"art:NAME\".", out error);
                return new ParsedWrite { Kind = ItemKinds.Boxes, Id = ItemKinds.SingletonId, BaseVersion = write.BaseVersion, ValueJson = write.Value!.Value.GetRawText(), Boxes = boxes };

            case ItemKinds.Settings:
                if (write.Id != ItemKinds.SingletonId || write.Deleted) return Fail("There is one settings item, id 'bank', and it can't be deleted.", out error);
                if (write.Value is not { ValueKind: JsonValueKind.Object } settings) return Fail("Settings are an object.", out error);
                return new ParsedWrite { Kind = ItemKinds.Settings, Id = ItemKinds.SingletonId, BaseVersion = write.BaseVersion, ValueJson = settings.GetRawText() };

            default:
                return Fail($"Unknown item kind '{write.Kind}'.", out error);
        }
    }

    private static T? Decode<T>(JsonElement? value) where T : class
    {
        if (value is not { ValueKind: JsonValueKind.Object } element) return null;
        try { return element.Deserialize<T>(ProtocolJson.Options); }
        catch (JsonException) { return null; }
    }

    private static ParsedWrite? Fail(string message, out string error)
    {
        error = message;
        return null;
    }
}
