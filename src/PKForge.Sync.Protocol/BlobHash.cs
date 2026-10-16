using System.Security.Cryptography;

namespace PKForge.Sync.Protocol;

/// <summary>Blob ids: lowercase hex SHA-256 of the Pokémon bytes.</summary>
public static class BlobHash
{
    public static string Compute(ReadOnlySpan<byte> data) => Convert.ToHexStringLower(SHA256.HashData(data));

    public static bool IsValid(string? hash) =>
        hash is { Length: 64 } && hash.All(c => c is >= '0' and <= '9' or >= 'a' and <= 'f');
}
