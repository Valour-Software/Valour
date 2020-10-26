using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;

namespace Valour.Shared.Users
{
    public static class PasswordManager
    {
        static readonly HashAlgorithmName HashName = HashAlgorithmName.SHA256;
        const int Iterations = 30000;

        public static byte[] GetHashForPassword(string password, string salt)
        {
            byte[] passBytes = Encoding.Unicode.GetBytes(password);
            byte[] saltBytes = Encoding.Unicode.GetBytes(salt);

            using (var deriveBytes = new Rfc2898DeriveBytes(passBytes, saltBytes, Iterations, HashName))
            {
                return deriveBytes.GetBytes(128);
            }
        }
    }
}
