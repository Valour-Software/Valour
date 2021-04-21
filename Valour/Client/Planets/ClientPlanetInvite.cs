using System;
using AutoMapper;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Valour.Shared;
using Valour.Client;
using Newtonsoft.Json;

namespace Valour.Shared.Planets
{
    /*  Valour - A free and secure chat client
     *  Copyright (C) 2021 Vooper Media LLC
     *  This program is subject to the GNU Affero General Public license
     *  A copy of the license should be included - if not, see <http://www.gnu.org/licenses/>
     */


    /// <summary>
    /// This represents a user within a planet and is used to represent membership
    /// </summary>
    public class ClientPlanetInvite : PlanetInvite
    {
        /// <summary>
        /// Returns the planet name for the invite
        /// </summary>
        public async Task<string> GetPlanetName()
        {
            string json = await ClientUserManager.Http.GetStringAsync($"Invite/GetPlanetName?invite_code={Code}");

            TaskResult<string> result = JsonConvert.DeserializeObject<TaskResult<string>>(json);

            if (result == null)
            {
                Console.WriteLine($"Critical error retrieving planet name for invite with code {Code}");
                return "Error";
            }

            if (!result.Success)
            {
                Console.WriteLine($"Error retrieving planet name for invite with code {Code}");
                Console.WriteLine(result.Message);
                return "Error";
            }

            return result.Data;
        }

        /// <summary>
        /// Returns the planet icon for the invite
        /// </summary>
        public async Task<string> GetPlanetIcon()
        {
            string json = await ClientUserManager.Http.GetStringAsync($"Invite/GetPlanetIcon?invite_code={Code}");

            TaskResult<string> result = JsonConvert.DeserializeObject<TaskResult<string>>(json);

            if (result == null)
            {
                Console.WriteLine($"Critical error retrieving planet icon for invite with code {Code}");
                return "Error";
            }

            if (!result.Success)
            {
                Console.WriteLine($"Error retrieving planet icon for invite with code {Code}");
                Console.WriteLine(result.Message);
                return "Error";
            }

            return result.Data;
        }

        public static ClientPlanetInvite FromBase(PlanetInvite invite, IMapper mapper)
        {
            return mapper.Map<ClientPlanetInvite>(invite);
        }
    }
}