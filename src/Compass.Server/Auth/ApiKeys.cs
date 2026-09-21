using System.Security.Cryptography;
using System.Text;

namespace Compass.Server.Auth;

/// <summary>Generation and verification of account secret keys.</summary>
public static class ApiKeys
{
    /// <summary>Prefix so a leaked key is instantly recognisable in a log or a pasted message.</summary>
    public const string Prefix = "cmps_";

    private const int EntropyBytes = 32;

    /// <summary>Mints a new key. Returned once, never stored in this form.</summary>
    public static string Generate()
    {
        var bytes = RandomNumberGenerator.GetBytes(EntropyBytes);
        return Prefix + Base64UrlEncode(bytes);
    }

    /// <summary>
    /// Hashes a key for storage and lookup.
    ///
    /// A single SHA-256 is the right primitive here, not Argon2 or PBKDF2: those exist to slow down
    /// guessing of low-entropy human passwords, and this is 256 bits of CSPRNG output. Stretching it
    /// would add latency to every request and buy nothing.
    /// </summary>
    public static string Hash(string key)
    {
        var digest = SHA256.HashData(Encoding.UTF8.GetBytes(key));
        return Convert.ToBase64String(digest);
    }

    /// <summary>Cheap shape check, so obviously malformed keys never reach the database.</summary>
    public static bool LooksValid(string? key) =>
        !string.IsNullOrWhiteSpace(key)
        && key.StartsWith(Prefix, StringComparison.Ordinal)
        && key.Length is > 20 and < 128;

    private static string Base64UrlEncode(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
