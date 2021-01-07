using AutoMapper;
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
    /// This class exists to add client funtionality to the PlanetUser
    /// class. It does not, and should not, have any extra fields.
    /// Just helper methods and properties.
    /// </summary>
    public class ClientPlanetUser : PlanetUser
    {

        /// <summary>
        /// Returns the generic planetuser object
        /// </summary>
        public PlanetUser PlanetUser
        {
            get
            {
                return (PlanetUser)this;
            }
        }

        /// <summary>
        /// Returns the client version from the base
        /// </summary>
        public static ClientPlanetUser FromBase(PlanetUser planetuser, IMapper mapper)
        {
            return mapper.Map<ClientPlanetUser>(planetuser);
        }

        /// <summary>
        /// Returns a 
        /// </summary>
        /// <param name="userid"></param>
        /// <param name="planetid"></param>
        /// <returns></returns>
        public static async Task<ClientPlanetUser> GetClientPlanetUserAsync(ulong userid, ulong planetid, IMapper mapper)
        {
            string json = await ClientUserManager.Http.GetStringAsync($"User/GetPlanetUser?userid={userid}&planetid={planetid}");

            PlanetUser result = JsonConvert.DeserializeObject<PlanetUser>(json);

            // Null check
            if (result == null) return null;

            ClientPlanetUser planetUser = mapper.Map<ClientPlanetUser>(result);

            return planetUser;
        }
    }
}
