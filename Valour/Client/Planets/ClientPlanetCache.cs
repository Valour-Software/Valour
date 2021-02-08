using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;

namespace Valour.Client.Planets
{
    /*  Valour - A free and secure chat client
     *  Copyright (C) 2021 Vooper Media LLC
     *  This program is subject to the GNU Affero General Public license
     *  A copy of the license should be included - if not, see <http://www.gnu.org/licenses/>
     */

    /// <summary>
    /// The clientside planet cache reduces the need to repeatedly ask the server
    /// for planet resources
    /// </summary>
    public class ClientPlanetCache
    {
        private static ConcurrentDictionary<ulong, ClientPlanet> Cache = new ConcurrentDictionary<ulong, ClientPlanet>();

        /// <summary>
        /// Returns a user from the given id
        /// </summary>
        public static async Task<ClientPlanet> GetPlanetAsync(ulong id)
        {
            // Attempt to retrieve from cache
            if (Cache.ContainsKey(id))
            {
                return Cache[id];
            }

            // Retrieve from server
            ClientPlanet planet = await ClientPlanet.GetClientPlanetAsync(id);

            if (planet == null)
            {
                Console.WriteLine($"Failed to fetch planet with id {id}.");
                return null;
            }

            // Add to cache
            Cache.TryAdd(id, planet);

            return planet;

        }
    }
}
