using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using System.Text.RegularExpressions;

namespace Valour.Shared.Users.Identity
{
    /*  Valour - A free and secure chat client
     *  Copyright (C) 2021 Vooper Media LLC
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
