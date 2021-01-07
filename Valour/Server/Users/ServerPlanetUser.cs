using AutoMapper;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Valour.Server.Database;
using Valour.Shared.Users;

namespace Valour.Server.Users
{
    /*  Valour - A free and secure chat client
     *  Copyright (C) 2020 Vooper Media LLC
     *  This program is subject to the GNU Affero General Public license
     *  A copy of the license should be included - if not, see <http://www.gnu.org/licenses/>
     */

    /// <summary>
    /// This class exists to add server funtionality to the PlanetUser
    /// class. It does not, and should not, have any extra fields.
    /// Just helper methods and properties.
    /// </summary>
    public class ServerPlanetUser : PlanetUser
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
        /// Returns a ServerPlanet using a Planet as a base
        /// </summary>
        public static ServerPlanetUser FromBase(PlanetUser planetuser, IMapper mapper)
        {
            return mapper.Map<ServerPlanetUser>(planetuser);
        }

        /// <summary>
        /// Creates a PlanetUser instance using a user and planet
        /// </summary>
        public static async Task<ServerPlanetUser> CreateAsync(User user, PlanetUser planet)
        {
            return await CreateAsync(user.Id, planet.Id);
        }

        /// <summary>
        /// Creates a PlanetUser instance using a user id and planet id
        /// </summary>
        public static async Task<ServerPlanetUser> CreateAsync(ulong userid, ulong planetid)
        {
            using (ValourDB db = new ValourDB(ValourDB.DBOptions))
            {
                // Find server user data
                ClientUser preUser = await db.Users.FindAsync(userid);

                // Null check
                if (preUser == null) return null;

                // Clear private info
                User user = 
            }
        }
    }
}
