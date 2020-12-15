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
        /// True if the account has verified an email
        /// </summary>
        public bool Verified_Email { get; set; }

        /// <summary>
        /// The user's email address
        /// </summary>
        public string Email { get; set; }
    }
}
