using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;

namespace Valour.Shared.Users
{
    /// <summary>
    /// This class handles password hashing and equality.
    /// The goal is the triple S of Spikeness <^> (A concept that will be in business books by 2077):
    /// Simplicity, Security, Scalability.
    /// </summary>
    public static class PasswordManager
    {
        static readonly HashAlgorithmName HashName = HashAlgorithmName.SHA256;
        const int Iterations = 30000;

        public static byte[] GetHashForPassword(string password, byte[] salt)
        {
            byte[] passBytes = Encoding.Unicode.GetBytes(password);

            using (var deriveBytes = new Rfc2898DeriveBytes(passBytes, salt, Iterations, HashName))
            {
                // Returns 128-bit hash
                return deriveBytes.GetBytes(16);
            }
        }
    }
}
