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
    public static class UserCache
    {
        private static Dictionary<ulong, User> Cache = new Dictionary<ulong, User>();

        /// <summary>
        /// Returns a user from the given id
        /// </summary>
        public static async Task<User> GetUserAsync(ulong id)
        {
            // Attempt to retrieve from cache
            if (Cache.ContainsKey(id))
            {
                return Cache[id];
            }

            // Retrieve from server
            string json = await ClientUserManager.Http.GetStringAsync($"User/GetUser?id={id}");

            User user = JsonConvert.DeserializeObject<User>(json);

            if (user == null)
            {
                Console.WriteLine($"Failed to fetch user with id {id}");
            }

            // Add to cache
            Cache.Add(id, user);

            return user;
        }
    }
}
