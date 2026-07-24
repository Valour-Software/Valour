using System.Security.Cryptography;
using System.Text;

namespace Valour.Server.Utilities;

/// <summary>
/// Constant-time comparison for secrets supplied by a caller (client secrets,
/// webhook tokens, and similar). Ordinary string equality returns as soon as it
/// finds a differing character, which leaks how much of the secret was correct.
/// </summary>
public static class SecretComparer
{
    public static bool Equals(string? expected, string? provided)
    {
        if (expected is null || provided is null)
            return false;

        // Hashing both sides first means the comparison length is fixed, so
        // the secret's length does not leak either.
        var expectedHash = SHA256.HashData(Encoding.UTF8.GetBytes(expected));
        var providedHash = SHA256.HashData(Encoding.UTF8.GetBytes(provided));

        return CryptographicOperations.FixedTimeEquals(expectedHash, providedHash);
    }
}
