using Newtonsoft.Json;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Valour.Shared.Users;
using Valour.Shared;

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
        private static ConcurrentDictionary<(ulong, ulong), ClientPlanetUser> Cache = new ConcurrentDictionary<(ulong, ulong), ClientPlanetUser>();

        public static async Task<List<ClientPlanetUser>> GetPlanetsUsers(ulong planet_id)
        {
            string json = await ClientUserManager.Http.GetStringAsync($"User/GetPlanetUsers?Planet_Id={planet_id}&token={ClientUserManager.UserSecretToken}&userid={ClientUserManager.User.Id}");

            TaskResult<List<ClientPlanetUser>> result = JsonConvert.DeserializeObject<TaskResult<List<ClientPlanetUser>>>(json);

            List<ClientPlanetUser> list = result.Data;

            foreach(ClientPlanetUser user in list) {
                if (Cache.ContainsKey((user.Id, planet_id)) == false) {
                    Cache.TryAdd((user.Id, planet_id), user);
                }
            }
            
            return list;
   }

        /// <summary>
        /// Returns a user from the given id
        /// </summary>
        public static async Task<ClientPlanetUser> GetPlanetUserAsync(ulong userid, ulong planet_id)
        {
            if (userid == 0)
            {
                return new ClientPlanetUser()
                {
                    Id = 0,
                    Join_DateTime = DateTime.UtcNow,
                    Planet_Id = planet_id,
                    Username = "Valour AI"
                };
            }

            // Attempt to retrieve from cache
            if (Cache.ContainsKey((userid, planet_id)))
            {
                return Cache[(userid, planet_id)];
            }

            // Retrieve from server
            ClientPlanetUser user = await ClientPlanetUser.GetClientPlanetUserAsync(userid, planet_id);

            if (user == null)
            {
                Console.WriteLine($"Failed to fetch planet user with user id {userid} and planet id {planet_id}.");
                return null;
            }

            Console.WriteLine($"Fetched planet user {userid} for planet {planet_id}");

            // Add to cache
            Cache.TryAdd((userid, planet_id), user);

            return user;

        }
    }
}
