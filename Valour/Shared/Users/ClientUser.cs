using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Valour.Shared.Users;

namespace Valour.Shared.Users
{
    /// <summary>
    /// This is the private User implementation, which should only be held by the server and local client.
    /// </summary>
    public class ClientUser : User
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
