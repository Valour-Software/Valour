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
        private static Dictionary<(ulong, ulong), PlanetUser> Cache = new Dictionary<(ulong, ulong), PlanetUser>();

        /// <summary>
        /// Returns a user from the given id
        /// </summary>
        public static async Task<PlanetUser> GetPlanetUserAsync(ulong userid, ulong planetid)
        {
            // Attempt to retrieve from cache
            if (Cache.ContainsKey((userid, planetid)))
            {
                return Cache[(userid, planetid)];
            }

            // Retrieve from server
            string json = await ClientUserManager.Http.GetStringAsync($"User/GetPlanetUser?userid={userid}&planetid={planetid}");

            PlanetUser user = JsonConvert.DeserializeObject<PlanetUser>(json);

            if (user == null)
            {
                Console.WriteLine($"Failed to fetch planet user with user id {userid} and planet id {planetid}.");
            }

            // Add to cache
            Cache.Add((userid, planetid), user);

            return user;
        }
    }
}
