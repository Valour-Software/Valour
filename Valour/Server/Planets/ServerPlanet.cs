using AutoMapper;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Valour.Client.Users;
using Valour.Server.Database;
using Valour.Server.Oauth;
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
        /// Returns a ServerPlanet using a Planet as a base
        /// </summary>
        public static ServerPlanet FromBase(Planet planet, IMapper mapper)
        {
            return mapper.Map<ServerPlanet>(planet);
        }

        /// <summary>
        /// Retrieves a ServerPlanet for the given id
        /// </summary>
        public static async Task<ServerPlanet> FindAsync(ulong id, IMapper mapper)
        {
            using (ValourDB db = new ValourDB(ValourDB.DBOptions))
            {
                Planet planet = await db.Planets.FindAsync(id);
                return ServerPlanet.FromBase(planet, mapper);
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
        public async Task<bool> AuthorizedAsync(string token, Permission permission)
        {
            if (permission.Value == PlanetPermissions.View.Value)
            {
                if (Public) return true;
            }

            using (ValourDB db = new ValourDB(ValourDB.DBOptions))
            {
                AuthToken authToken = await db.AuthTokens.FindAsync(token);

                return await AuthorizedAsync(authToken, permission);
            }
        }

        /// <summary>
        /// Returns if the given user is authorized to access this planet
        /// </summary>
        public async Task<bool> AuthorizedAsync(AuthToken authToken, Permission permission)
        {
            if (permission.Value == PlanetPermissions.View.Value)
            {
                if (Public || (await IsMemberAsync(authToken.User_Id)))
                {
                    return true;
                }
                
            }

            if (authToken == null)
            {
                return false;
            }
            else
            {

                // Owner has all permissions
                if (authToken.User_Id == Owner_Id)
                {
                    return true;
                }

                // In the future we do role magic here
                PlanetUser user = await PlanetUserCache.GetPlanetUserAsync(authToken.User_Id, Id);
            }

            return false;
        }
    }
}
