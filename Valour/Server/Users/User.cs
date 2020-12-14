using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Valour.Shared.Users;

namespace Valour.Server.Users
{
    /// <summary>
    /// This is the server-side User implementation. It contains all content from the Client-side,
    /// and includes all hidden fields needed for server functions.
    /// </summary>
    public class User : ClientUser
    {
        /// <summary>
        /// The user password hash. This should NOT be able to be reached by the client,
        /// and should be 16 bytes (128 bits)
        /// </summary>
        public byte[] Password_Hash { get; set; }

        /// <summary>
        /// The unique salt for the password.
        /// Not to be confused with league of legends players.
        /// </summary>
        public byte[] Salt { get; set; }

        /// <summary>
        /// Will return true if the password is correct for the user
        /// </summary>
        public bool VerifyPassword(string password)
        {
            return PasswordManager.GetHashForPassword(password, Salt) == Password_Hash;
        }
    }
}
