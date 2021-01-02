using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Valour.Server.Database;
using Valour.Shared.Planets;
using Valour.Shared.Users;

namespace Valour.Server.Planets
{
    /*  Valour - A free and secure chat client
     *  Copyright (C) 2020 Vooper Media LLC
     *  This program is subject to the GNU Affero General Public license
     *  A copy of the license should be included - if not, see <http://www.gnu.org/licenses/>
     */

    /// <summary>
    /// This class exists to add server funtionality to the Planet
    /// class. It does not, and should not, have any extra fields or properties.
    /// Just helper methods.
    /// </summary>
    public class ServerPlanet : Planet
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
        /// Retrieves a ServerPlanet for the given id
        /// </summary>
        public static async Task<ServerPlanet> FindAsync(ulong id)
        {
            using (ValourDB db = new ValourDB(ValourDB.DBOptions))
            {
                return (ServerPlanet)(await db.Planets.FindAsync(id));
            }
        }

        /// <summary>
        /// Returns if a given user id is a member (async)
        /// </summary>
        public async Task<bool> IsMemberAsync(ulong userid)
        {
            using (ValourDB db = new ValourDB(ValourDB.DBOptions))
            {
                return await db.PlanetMembers.AnyAsync(x => x.Planet_Id == this.Id && x.User_Id == userid);
            }
        }

        /// <summary>
        /// Returns if a given user is a member (async)
        /// </summary>
        public async Task<bool> IsMemberAsync(User user)
        {
            return await IsMemberAsync(user.Id);
        }

        /// <summary>
        /// Returns if a given user id is a member
        /// </summary>
        public bool IsMember(ulong userid)
        {
            return IsMemberAsync(userid).Result;
        }

        /// <summary>
        /// Returns if a given user is a member
        /// </summary>
        public bool IsMember(User user)
        {
            return IsMember(user.Id);
        }
    }
}
