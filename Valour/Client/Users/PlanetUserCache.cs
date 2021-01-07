using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Valour.Shared.Users;

namespace Valour.Client.Users
{
    /*  Valour - A free and secure chat client
     *  Copyright (C) 2020 Vooper Media LLC
     *  This program is subject to the GNU Affero General Public license
     *  A copy of the license should be included - if not, see <http://www.gnu.org/licenses/>
     */

    /// <summary>
    /// A cache used on the client to prevent the need to repeatedly hit Valour servers
    /// for user data.
    /// </summary>
    public static class PlanetUserCache
    {
        private static Dictionary<(ulong, ulong), ClientPlanetUser> Cache = new Dictionary<(ulong, ulong), ClientPlanetUser>();

        /// <summary>
        /// Returns a user from the given id
        /// </summary>
        public static async Task<ClientPlanetUser> GetPlanetUserAsync(ulong userid, ulong planetid)
        {
            // Attempt to retrieve from cache
            if (Cache.ContainsKey((userid, planetid)))
            {
                return Cache[(userid, planetid)];
            }

            // Retrieve from server
            ClientPlanetUser user = await ClientPlanetUser.GetClientPlanetUserAsync(userid, planetid);

            if (user == null)
            {
                Console.WriteLine($"Failed to fetch planet user with user id {userid} and planet id {planetid}.");
                return null;
            }

            // Add to cache
            Cache.Add((userid, planetid), user);

            return user;
        }
    }
}
