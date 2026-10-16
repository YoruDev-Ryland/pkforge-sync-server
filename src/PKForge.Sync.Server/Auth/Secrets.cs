using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text;

namespace PKForge.Sync.Server.Auth;

public static class Secrets
{
    public const string PairingAlphabet = "ABCDEFGHJKMNPQRSTUVWXYZ23456789";
    public const int PairingCodeLength = 8;

    /// <summary>A 256-bit random key, base64url.</summary>
    public static string NewKey() => Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(32));

    /// <summary>SHA-256 hex. Keys and tokens are high-entropy, so a fast hash is enough.</summary>
    public static string Hash(string secret) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(secret)));

    public static string NewPairingCode() => RandomNumberGenerator.GetString(PairingAlphabet, PairingCodeLength);

    /// <summary>Accepts what people type: any case, spaces or dashes.</summary>
    public static string NormalizePairingCode(string code) =>
        new(code.Where(char.IsLetterOrDigit).Select(char.ToUpperInvariant).ToArray());
}
