using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using System.Text.RegularExpressions;

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
        const int HASH_SIZE = 32;

        public static byte[] GetHashForPassword(string password, byte[] salt)
        {
            byte[] passBytes = Encoding.Unicode.GetBytes(password);

            using (var deriveBytes = new Rfc2898DeriveBytes(passBytes, salt, Iterations, HashName))
            {
                // Returns 256-bit hash
                return deriveBytes.GetBytes(HASH_SIZE);
            }
        }

        public static Regex hasUpper = new Regex(@"/[A-Z]/");
        public static Regex hasLower = new Regex(@"/[a-z]/");
        public static Regex hasNumbers = new Regex(@"/\d/");
        public static Regex hasSymbols = new Regex(@"/\W/");

        /// <summary>
        /// Returns success if a password meets complexity rules
        /// </summary>
        public static TaskResult TestComplexity(string password)
        {
            if (password.Length < 12)
            {
                return new TaskResult(false, $"Failed: Please use a password at least 12 characters in length.");
            }

            if (!hasUpper.IsMatch(password))
            {
                return new TaskResult(false, $"Failed: Please use a password that contains an uppercase character.");
            }

            if (!hasLower.IsMatch(password))
            {
                return new TaskResult(false, $"Failed: Please use a password that contains an lowercase character.");
            }

            if (!hasNumbers.IsMatch(password))
            {
                return new TaskResult(false, $"Failed: Please use a password that contains a number.");
            }

            if (!hasSymbols.IsMatch(password))
            {
                return new TaskResult(false, $"Failed: Please use a password that contains a symbol.");
            }

            return new TaskResult(true, $"Success: The given password passed all tests.");
        }

        /// <summary>
        /// Generates random salt for use in passwords
        /// </summary>
        public static void GenerateSalt(byte[] salt)
        {
            using (var random = new RNGCryptoServiceProvider())
            {
                random.GetNonZeroBytes(salt);
            }
        }
    }
}
