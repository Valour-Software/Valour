using System;
using AutoMapper;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Valour.Shared;
using Valour.Client;
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
            var response = await ClientUserManager.Http.GetAsync($"api/invite/{Code}/planet/name");
            var message = await response.Content.ReadAsStringAsync();
            
            if (!response.IsSuccessStatusCode)
            {
                Console.WriteLine($"Critical error retrieving planet name for invite with code {Code}");
                Console.WriteLine(message);
                return "Error";
            }
            
            return message;
        }

        /// <summary>
        /// Returns the planet icon for the invite
        /// </summary>
        public async Task<string> GetPlanetIcon()
        {
            var response = await ClientUserManager.Http.GetAsync($"api/invite/{Code}/planet/icon_url");
            
            var message = await response.Content.ReadAsStringAsync();
            
            if (!response.IsSuccessStatusCode)
            {
                Console.WriteLine($"Critical error retrieving planet name for invite with code {Code}");
                Console.WriteLine(message);
                return "Error";
            }
            
            return message;
        }
    }
}