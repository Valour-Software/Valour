using AutoMapper;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Valour.Client.Planets;
using Valour.Shared.Categories;

namespace Valour.Client.Categories
{
    /*  Valour - A free and secure chat client
     *  Copyright (C) 2021 Vooper Media LLC
     *  This program is subject to the GNU Affero General Public license
     *  A copy of the license should be included - if not, see <http://www.gnu.org/licenses/>
     */

    /// <summary>
    /// This class exists to add client funtionality to the PlanetCategory
    /// class. It does not, and should not, have any extra fields or properties.
    /// Just helper methods.
    /// </summary>
    public class ClientPlanetCategory : PlanetCategory
    {
        /// <summary>
        /// Converts to a client version of planet category
        /// </summary>
        public static ClientPlanetCategory FromBase(PlanetCategory channel, IMapper mapper)
        {
            return mapper.Map<ClientPlanetCategory>(channel);
        }

        /// <summary>
        /// Returns the planet this category belongs to
        /// </summary>
        public async Task<ClientPlanet> GetPlanetAsync()
        {
            return await ClientPlanetManager.Current.GetPlanetAsync(Planet_Id);
        }
    }
}
