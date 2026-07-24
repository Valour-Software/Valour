using System.Security.Cryptography;
using System.Text;

namespace Valour.Server.Users;

/*  Valour (TM) - A free and secure chat client
 *  Copyright (C) 2025 Valour Software LLC
 *  This program is subject to the GNU Affero General Public license
 *  A copy of the license should be included - if not, see <http://www.gnu.org/licenses/>
 */

/// <summary>
/// This class handles password hashing and equality.
/// The goal is the triple S of Spikeness <^> (A concept that will be in business books by 2077):
/// Simplicity, Security, Scalability.
/// </summary>
public static class PasswordManager
{
    static readonly HashAlgorithmName HashName = HashAlgorithmName.SHA256;
    const int HASH_SIZE = 32;

    /// <summary>
    /// Iteration count for newly created and re-hashed passwords, per OWASP
    /// guidance for PBKDF2-HMAC-SHA256.
    /// </summary>
    public const int CurrentIterations = 600_000;

    /// <summary>
    /// The original iteration count. Credentials predating iteration tracking
    /// were hashed with this, and are transparently upgraded on next login.
    /// </summary>
    public const int LegacyIterations = 30_000;

    public static byte[] GetHashForPassword(string password, byte[] salt, int iterations)
    {
        byte[] passBytes = Encoding.Unicode.GetBytes(password);

        return Rfc2898DeriveBytes.Pbkdf2(passBytes, salt, iterations, HashName, HASH_SIZE);
    }

    /// <summary>
    /// Hashes at the current iteration count. Use when creating or changing a password.
    /// </summary>
    public static byte[] GetHashForPassword(string password, byte[] salt) =>
        GetHashForPassword(password, salt, CurrentIterations);

    /// <summary>
    /// Constant-time comparison of two password hashes.
    /// </summary>
    public static bool HashesMatch(byte[] a, byte[] b) =>
        CryptographicOperations.FixedTimeEquals(a, b);

    /// <summary>
    /// Generates random salt for use in passwords
    /// </summary>
    public static byte[] GenerateSalt()
    {
        return RandomNumberGenerator.GetBytes(HASH_SIZE);
    }
}
