using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Valour.Shared.Planets;
using Valour.Shared.Users;

namespace Valour.Shared.Users
{
    /*  Valour - A free and secure chat client
     *  Copyright (C) 2020 Vooper Media LLC
     *  This program is subject to the GNU Affero General Public license
     *  A copy of the license should be included - if not, see <http://www.gnu.org/licenses/>
     */

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

        /// <summary>
        /// Cache for joined planet objects
        /// </summary>
        public List<Planet> Planets = new List<Planet>();

        public async Task RefreshPlanets()
        {

        }
    }
}
