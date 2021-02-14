using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations.Schema;
using System.Linq;
using System.Threading.Tasks;
using Valour.Server.Database;
using Valour.Server.Mapping;
using Valour.Server.Roles;
using Valour.Server.Users;
using Valour.Shared.Planets;
using Valour.Shared.Roles;
using Valour.Shared.Users;

/*  Valour - A free and secure chat client
 *  Copyright (C) 2021 Vooper Media LLC
 *  This program is subject to the GNU Affero General Public license
 *  A copy of the license should be included - if not, see <http://www.gnu.org/licenses/>
 */

namespace Valour.Server.Planets
{
    /// <summary>
    /// This class exists to add server funtionality to the PlanetMember
    /// class.
    /// </summary>
    public class ServerPlanetMember : PlanetMember
    {

        // Relational DB stuff
        [ForeignKey("User_Id")]
        public virtual ServerUser User { get; set; }

        [InverseProperty("Member")]
        public virtual ICollection<ServerPlanetRoleMember> RoleMembership { get; set; }

        /// <summary>
        /// Returns the generic planet member object
        /// </summary>
        public PlanetMember GetPlanetMember()
        {
            return (PlanetMember)this;
        }

        /// <summary>
        /// Returns a ServerPlanet using a Planet as a base
        /// </summary>
        public static ServerPlanetMember FromBase(PlanetMember member)
        {
            return MappingManager.Mapper.Map<ServerPlanetMember>(member);
        }

        public static async Task<ServerPlanetMember> FindAsync(ulong user_id, ulong planet_id)
        {
            using (ValourDB db = new ValourDB(ValourDB.DBOptions))
            {
                return FromBase(await db.PlanetMembers.FirstOrDefaultAsync(x => x.Planet_Id == planet_id &&
                                                                                x.User_Id == user_id));
            }
        }

        /// <summary>
        /// Returns all of the roles for a planet user
        /// </summary>
        public async Task<List<PlanetRole>> GetRolesAsync()
        {
            using (ValourDB Context = new ValourDB(ValourDB.DBOptions))
            {
                List<PlanetRole> roles = new List<PlanetRole>();

                // Add default role
                ServerPlanet planet = await ServerPlanet.FindAsync(Planet_Id);

                var membership = Context.PlanetRoleMembers.Where(x => x.User_Id == User_Id && x.Planet_Id == Planet_Id);

                foreach (var member in membership)
                {
                    PlanetRole role = await Context.PlanetRoles.FindAsync(member.Role_Id);

                    if (role != null && !roles.Contains(role))
                    {
                        roles.Add(role);
                    }
                }

                // Put most important roles at start
                roles.OrderByDescending(x => x.Authority);

                return roles;
            }
        }

        /// <summary>
        /// Returns all of the roles for a planet user (helper for client work)
        /// </summary>
        public async Task<List<PlanetRole>> GetClientRolesAsync()
        {
            using (ValourDB Context = new ValourDB(ValourDB.DBOptions))
            {
                List<PlanetRole> roles = new List<PlanetRole>();

                // Add default role
                ServerPlanet planet = await ServerPlanet.FindAsync(Planet_Id);
                roles.Add(await planet.GetDefaultRole());

                var membership = Context.PlanetRoleMembers.Where(x => x.User_Id == User_Id && x.Planet_Id == Planet_Id);

                foreach (var member in membership)
                {
                    PlanetRole role = await Context.PlanetRoles.FindAsync(member.Role_Id);

                    if (role != null && !roles.Contains(role))
                    {
                        roles.Add(role);
                    }
                }

                // Put most important roles at start
                roles.OrderByDescending(x => x.Authority);

                return roles;
            }
        }

        /// <summary>
        /// Returns the user (async)
        /// </summary>
        public async Task<User> GetUserAsync()
        {
            using (ValourDB db = new ValourDB(ValourDB.DBOptions))
            {
                return await db.Users.FindAsync(User_Id);
            }
        }

        /// <summary>
        /// Returns the user
        /// </summary>
        public User GetUser()
        {
            return GetUserAsync().Result;
        }

        /// <summary>
        /// Returns the planet (async)
        /// </summary>
        public async Task<Planet> GetPlanetAsync()
        {
            using (ValourDB db = new ValourDB(ValourDB.DBOptions))
            {
                return await db.Planets.FindAsync(Planet_Id);
            }
        }

        /// <summary>
        /// Returns the planet
        /// </summary>
        public Planet GetPlanet()
        {
            return GetPlanetAsync().Result;
        }
    }
}
