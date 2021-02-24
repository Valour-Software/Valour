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
using Valour.Shared.Oauth;
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

        [ForeignKey("Planet_Id")]
        public virtual ServerPlanet Planet { get; set; }

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
        public async Task<List<ServerPlanetRole>> GetRolesAsync(ValourDB db = null)
        {
            bool createdb = false;
            if (db == null)
            {
                db = new ValourDB(ValourDB.DBOptions);
                createdb = true;
            }

            List<ServerPlanetRole> roles = new List<ServerPlanetRole>();

            // Add default role
            ServerPlanet planet = await ServerPlanet.FindAsync(Planet_Id);

            var membership = db.PlanetRoleMembers.Include(x => x.Role)
                                                 .Where(x => x.Member_Id == Id)
                                                 .OrderBy(x => x.Role.Position)
                                                 .Select(x => x.Role)
                                                 .ToList();

            if (createdb)
            {
                await db.DisposeAsync();
            }

            return membership;
        }

        /// <summary>
        /// Returns the member's primary role
        /// </summary>
        public async Task<ServerPlanetRole> GetPrimaryRoleAsync(ValourDB db = null)
        {
            bool createdb = false;
            if (db == null)
            {
                db = new ValourDB(ValourDB.DBOptions);
                createdb = true;
            }

            var membership = db.PlanetRoleMembers.Include(x => x.Role).Where(x => x.Member_Id == Id).OrderBy(x => x.Role.Position);
            var primary = (await membership.FirstOrDefaultAsync()).Role;

            if (createdb)
            {
                await db.DisposeAsync();
            }

            return primary;
        }

        /// <summary>
        /// Returns if the member has the given permission
        /// </summary>
        public async Task<bool> HasPermissionAsync(PlanetPermission permission, ValourDB db = null)
        {
            // Make sure we didn't include the planet already
            if (Planet == null)
            {
                bool createdb = false;
                if (db == null)
                {
                    db = new ValourDB(ValourDB.DBOptions);
                    createdb = true;
                }

                Planet = await db.Planets.FindAsync(Planet_Id);

                if (createdb)
                {
                    await db.DisposeAsync();
                }
            }

            // Special case for owner
            if (User_Id == Planet.Owner_Id)
            {
                return true;
            }

            return (await GetPrimaryRoleAsync(db)).HasPermission(permission);
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
