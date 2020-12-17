using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Valour.Shared.Users;

namespace Valour.Client
{
    /*  Valour - A free and secure chat client
     *  Copyright (C) 2020 Vooper Media LLC
     *  This program is subject to the GNU Affero General Public license
     *  A copy of the license should be included - if not, see <http://www.gnu.org/licenses/>
     */

    /// <summary>
    /// The ClientUserManager provides important User controls for
    /// use on the client. It is static to allow easy access to user data.
    /// </summary>
    public static class ClientUserManager
    {
        /// <summary>
        /// This is the currently logged in user for the client
        /// </summary>
        public static ClientUser User { get; set; }

        /// <summary>
        /// This is the token the user is currently using to stay logged in
        /// </summary>
        public static string UserSecretToken { get; set; }

        /// <summary>
        /// True if the user is logged in
        /// </summary>
        public static bool IsLoggedIn()
        {
            return !(User == null);
        }

        /// <summary>
        /// Initializes the user using a valid user token
        /// </summary>
        /// <param name="token"></param>
        public static void InitializeUser(string token)
        {

        }
    }
}
