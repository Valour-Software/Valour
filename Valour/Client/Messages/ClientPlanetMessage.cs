using AutoMapper;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Valour.Client.Users;
using Valour.Shared.Messages;
using Valour.Shared.Users;

namespace Valour.Client.Messages
{
    /*  Valour - A free and secure chat client
     *  Copyright (C) 2020 Vooper Media LLC
     *  This program is subject to the GNU Affero General Public license
     *  A copy of the license should be included - if not, see <http://www.gnu.org/licenses/>
     */

    /// <summary>
    /// This class exists to add client funtionality to the PlanetMessage
    /// class. It does not, and should not, have any extra fields or properties.
    /// Just helper methods.
    /// </summary>
    public class ClientPlanetMessage : PlanetMessage
    {

        /// <summary>
        /// Returns the generic planet object
        /// </summary>
        public PlanetMessage PlanetMessage
        {
            get
            {
                return (PlanetMessage)this;
            }
        }

        /// <summary>
        /// Returns client version using shared implementation
        /// </summary>
        public static ClientPlanetMessage FromBase(PlanetMessage message, IMapper mapper)
        {
            return mapper.Map<ClientPlanetMessage>(message);
        }


        /// <summary>
        /// Returns the author of the message
        /// </summary>
        public async Task<ClientPlanetUser> GetAuthorAsync()
        {
            ClientPlanetUser planetUser = await PlanetUserCache.GetPlanetUserAsync(Author_Id, Planet_Id);

            return planetUser;
        }
    }
}
