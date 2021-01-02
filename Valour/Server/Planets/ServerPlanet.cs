using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Valour.Server.Database;
using Valour.Shared.Channels;
using Valour.Shared.Oauth;
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

        /// <summary>
        /// Returns the primary channel for the planet
        /// </summary>
        public async Task<PlanetChatChannel> GetPrimaryChannelAsync()
        {
            using (ValourDB db = new ValourDB(ValourDB.DBOptions))
            {
                // TODO: Make a way to choose a primary channel rather than just grabbing the first one
                return await db.PlanetChatChannels.Where(x => x.Planet_Id == this.Id).FirstAsync();
            }
        }

        /// <summary>
        /// Returns if the given user is authorized to access this planet
        /// </summary>
        public async Task<bool> AuthorizedAsync(ulong userid, string token)
        {
            // Need to ensure user is a member if not public
            if (!Public)
            {
                using (ValourDB db = new ValourDB(ValourDB.DBOptions))
                {
                    AuthToken authToken = await db.AuthTokens.FindAsync(token);

                    if (authToken == null || authToken.User_Id != userid)
                    {
                        return false;
                    }

                    // If the user is not a member, cancel
                    if (!(await IsMemberAsync(userid)))
                    {
                        return false;
                    }
                }
            }

            return true;
        }
    }
}
