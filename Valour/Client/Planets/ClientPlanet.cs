using AutoMapper;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using Valour.Shared;
using Valour.Shared.Channels;
using Valour.Shared.Planets;

namespace Valour.Client.Planets
{
    /*  Valour - A free and secure chat client
 *  Copyright (C) 2020 Vooper Media LLC
 *  This program is subject to the GNU Affero General Public license
 *  A copy of the license should be included - if not, see <http://www.gnu.org/licenses/>
 */

    /// <summary>
    /// This class exists to add client funtionality to the Planet
    /// class. It does not, and should not, have any extra fields or properties.
    /// Just helper methods.
    /// </summary>
    public class ClientPlanet : Planet
    {

        /// <summary>
        /// Returns the generic planet object
        /// </summary>
        public Planet Planet
        {
            get
            {
                return (Planet)this;
            }
        }

        /// <summary>
        /// Returns a ServerPlanet using a Planet as a base
        /// </summary>
        public static ClientPlanet FromBase(Planet planet, IMapper mapper)
        {
            return mapper.Map<ClientPlanet>(planet);
        }

        /// <summary>
        /// Returns the primary channel of the planet
        /// </summary>
        public async Task<PlanetChatChannel> GetPrimaryChannelAsync()
        {
            string json = await ClientUserManager.Http.GetStringAsync($"Planet/GetPrimaryChannel?planetid={Id}" +
                                                                                              $"&userid={ClientUserManager.User.Id}" +
                                                                                              $"&token={ClientUserManager.UserSecretToken}");

            TaskResult<PlanetChatChannel> channelResult = JsonConvert.DeserializeObject<TaskResult<PlanetChatChannel>>(json);

            if (channelResult == null)
            {
                Console.WriteLine($"Failed to retrieve primary channel for planet {Id}.");
                return null;
            }

            if (!channelResult.Success)
            {
                Console.WriteLine($"Failed to retrieve primary channel for planet {Id}: {channelResult.Message}");
            }

            return channelResult.Data;
        }
    }
}
