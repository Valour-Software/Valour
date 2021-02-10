using AutoMapper;
using Microsoft.AspNetCore.Mvc.Razor;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Valour.Server.Database;
using Valour.Server.Mapping;
using Valour.Server.Planets;
using Valour.Server.Roles;
using Valour.Shared.Planets;
using Valour.Shared.Roles;
using Valour.Shared.Users;

/*  Valour - A free and secure chat client
 *  Copyright (C) 2021 Vooper Media LLC
 *  This program is subject to the GNU Affero General Public license
 *  A copy of the license should be included - if not, see <http://www.gnu.org/licenses/>
 */

namespace Valour.Server.Users
{
    /*  Valour - A free and secure chat client
     *  Copyright (C) 2021 Vooper Media LLC
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
        public static ServerPlanetUser FromBase(PlanetUser planetuser)
        {
            return MappingManager.Mapper.Map<ServerPlanetUser>(planetuser);
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
        public static async Task<ServerPlanetUser> CreateAsync(ulong userid, ulong planet_id)
        {
            using (ValourDB db = new ValourDB(ValourDB.DBOptions))
            {
                // Retrieve user
                User user = await db.Users.FindAsync(userid);

                // Retrieve planet
                ServerPlanet planet = ServerPlanet.FromBase(await db.Planets.FindAsync(planet_id));

                // TODO: Actually set roles and stuff once roles exist.

                // Ensure user is within planet
                if (!(await planet.IsMemberAsync(user)))
                {
                    return null;
                }

                // First map the user to a planetUser to copy basic fields
                ServerPlanetUser planetUser = MappingManager.Mapper.Map<ServerPlanetUser>(user);

                // Now copy across planet info
                planetUser.Planet_Id = planet_id;

                return planetUser;
            }
        }

        /// <summary>
        /// Creates a PlanetUser instance using a user id and planet id
        /// </summary>
        public static async Task<ServerPlanetUser> CreateAsync(User user, ServerPlanet planet)
        {
            // Ensure user is within planet
            if (!(await planet.IsMemberAsync(user)))
            {
                return null;
            }

            // First map the user to a planetUser to copy basic fields
            ServerPlanetUser planetUser = MappingManager.Mapper.Map<ServerPlanetUser>(user);

            // Now copy across planet info
            planetUser.Planet_Id = planet.Id;

            return planetUser;
        }

        /// <summary>
        /// Returns all of the roles for a planet user
        /// </summary>
        public async Task<List<ServerPlanetRole>> GetRolesAsync()
        {
            using (ValourDB Context = new ValourDB(ValourDB.DBOptions))
            {
                List<ServerPlanetRole> roles = new List<ServerPlanetRole>();

                // Add default role
                ServerPlanet planet = await ServerPlanet.FindAsync(Planet_Id);
                roles.Add(await planet.GetDefaultRole());

                var membership = Context.PlanetRoleMembers.Where(x => x.User_Id == Id && x.Planet_Id == Planet_Id);

                foreach (var member in membership)
                {
                    PlanetRole role = await Context.PlanetRoles.FindAsync(member.Role_Id);

                    if (role != null && !roles.Contains(role))
                    {
                        roles.Add(ServerPlanetRole.FromBase(role));
                    }
                }

                // Put most important roles at start
                roles.OrderByDescending(x => x.Authority);

                return roles;
            }
        }
    }
}
