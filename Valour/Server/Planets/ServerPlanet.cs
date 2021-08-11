using AutoMapper;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Valour.Client.Users;
using Valour.Server.Database;
using Valour.Server.Mapping;
using Valour.Shared.Oauth;
using Valour.Server.Roles;
using Valour.Shared;
using Valour.Shared.Channels;
using Valour.Shared.Planets;
using Valour.Shared.Roles;
using Valour.Shared.Users;
using System.Text.Json.Serialization;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text.RegularExpressions;

namespace Valour.Server.Planets
{
    /*  Valour - A free and secure chat client
     *  Copyright (C) 2021 Vooper Media LLC
     *  This program is subject to the GNU Affero General Public license
     *  A copy of the license should be included - if not, see <http://www.gnu.org/licenses/>
     */

    /// <summary>
    /// This class exists to add server funtionality to the Planet class.
    /// </summary>
    public class ServerPlanet : Planet
    {
        [InverseProperty("Planet")]
        [JsonIgnore]
        public virtual ICollection<ServerPlanetRole> Roles { get; set; }

        [InverseProperty("Planet")]
        [JsonIgnore]
        public virtual ICollection<ServerPlanetMember> Members { get; set; }

        [InverseProperty("Planet")]
        public virtual ICollection<ServerPlanetChatChannel> ChatChannels { get; set; }

        public static Regex nameRegex = new Regex(@"^[a-zA-Z0-9 _-]+$");

        /// <summary>
        /// Validates that a given name is allowable for a server
        /// </summary>
        public static TaskResult ValidateName(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                return new TaskResult(false, "Planet names cannot be empty.");
            }

            if (name.Length > 32)
            {
                return new TaskResult(false, "Planet names must be 32 characters or less.");
            }

            if (!nameRegex.IsMatch(name))
            {
                return new TaskResult(false, "Planet names may only include letters, numbers, dashes, and underscores.");
            }

            return new TaskResult(true, "The given name is valid.");
        }

        /// <summary>
        /// Returns a ServerPlanet using a Planet as a base
        /// </summary>
        public static ServerPlanet FromBase(Planet planet)
        {
            return MappingManager.Mapper.Map<ServerPlanet>(planet);
        }

        /// <summary>
        /// Retrieves a ServerPlanet for the given id
        /// </summary>
        public static async Task<ServerPlanet> FindAsync(ulong id)
        {
            using (ValourDB db = new ValourDB(ValourDB.DBOptions))
            {
                Planet planet = await db.Planets.FindAsync(id);
                return ServerPlanet.FromBase(planet);
            }
        }

        /// <summary>
        /// Returns if a given user id is a member (async)
        /// </summary>
        public async Task<bool> IsMemberAsync(ulong user_id, ValourDB db = null)
        {
            // Setup db if none provided
            bool dbcreate = false;

            if (db == null)
            {
                db = new ValourDB(ValourDB.DBOptions);
                dbcreate = true;
            }

            var result = await db.PlanetMembers.AnyAsync(x => x.Planet_Id == this.Id && x.User_Id == user_id);

            // Clean up if created own db
            if (dbcreate) { await db.DisposeAsync(); }

            return result;
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
        public bool IsMember(ulong user_id)
        {
            return IsMemberAsync(user_id).Result;
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
                return await db.PlanetChatChannels.FindAsync(Main_Channel_Id);
            }
        }

        /// <summary>
        /// Returns if the given user is authorized to access this planet
        /// </summary>
        public async Task<bool> HasPermissionAsync(ServerPlanetMember member, PlanetPermission permission, ValourDB db)
        {
            // Special case for viewing planets
            if (permission.Value == PlanetPermissions.View.Value)
            {
                if (Public || (member != null))
                {
                    return true;
                }
            }

            // At this point all permissions require membership
            if (member == null)
            {
                return false;
            }

            // Owner has all permissions
            if (member.User_Id == Owner_Id)
            {
                return true;
            }

            // Get user main role
            var mainRole = await db.Entry(member).Collection(x => x.RoleMembership)
                                                 .Query()
                                                 .Include(x => x.Role)
                                                 .OrderBy(x => x.Role.Position)
                                                 .Select(x => x.Role)
                                                 .FirstAsync();

            // Return permission state
            return mainRole.HasPermission(permission);
        }

        /// <summary>
        /// Returns the default role for the planet
        /// </summary>
        public async Task<PlanetRole> GetDefaultRole()
        {
            using (ValourDB Context = new ValourDB(ValourDB.DBOptions))
            {
                return await Context.PlanetRoles.FindAsync(Default_Role_Id);
            }
        }

        /// <summary>
        /// Returns all roles within the planet
        /// </summary>
        public async Task<List<ServerPlanetRole>> GetRolesAsync(ValourDB db = null)
        {
            bool createdb = false;
            if (db == null)
            {
                db = new ValourDB(ValourDB.DBOptions);
                createdb = true;
            }

            var roles = db.PlanetRoles.Where(x => x.Planet_Id == Id).OrderBy(x => x.Position).ToList();

            if (createdb)
            {
                await db.DisposeAsync();
            }

            return roles;
        }

        /// <summary>
        /// Adds a member to the server
        /// </summary>
        public async Task AddMemberAsync(User user, ValourDB db = null)
        {

            // Setup db if none provided
            bool dbcreate = false;

            if (db == null)
            {
                db = new ValourDB(ValourDB.DBOptions);
                dbcreate = true;
            }

            // Already a member
            if (await db.PlanetMembers.AnyAsync(x => x.User_Id == user.Id && x.Planet_Id == Id))
            {
                return;
            }

            ServerPlanetMember member = new ServerPlanetMember()
            {
                Id = IdManager.Generate(),
                Nickname = user.Username,
                Planet_Id = Id,
                User_Id = user.Id
            };

            // Add to default planet role
            ServerPlanetRoleMember rolemember = new ServerPlanetRoleMember()
            {
                Id = IdManager.Generate(),
                Planet_Id = Id,
                User_Id = user.Id,
                Role_Id = Default_Role_Id,
                Member_Id = member.Id
            };

            await db.PlanetMembers.AddAsync(member);
            await db.PlanetRoleMembers.AddAsync(rolemember);
            await db.SaveChangesAsync();

            Console.WriteLine($"User {user.Username} ({user.Id}) has joined {Name} ({Id})");

            // Clean up if created own db
            if (dbcreate) { await db.DisposeAsync(); }
        }
    }
}
