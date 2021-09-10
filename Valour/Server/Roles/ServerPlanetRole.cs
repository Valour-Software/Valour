using System;
using Microsoft.EntityFrameworkCore;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Valour.Server.Database;
using Valour.Server.Mapping;
using Valour.Shared.Oauth;
using Valour.Shared.Roles;
using Valour.Server.Planets;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json.Serialization;
using Valour.Server.Categories;
using Valour.Shared;

/*  Valour - A free and secure chat client
 *  Copyright (C) 2021 Vooper Media LLC
 *  This program is subject to the GNU Affero General Public license
 *  A copy of the license should be included - if not, see <http://www.gnu.org/licenses/>
 */


namespace Valour.Server.Roles
{
    public class ServerPlanetRole : PlanetRole
    {
        [ForeignKey("Planet_Id")]
        [JsonIgnore]
        public virtual ServerPlanet Planet { get; set; }

        [InverseProperty("Role")]
        [JsonIgnore]
        public virtual ICollection<ServerChatChannelPermissionsNode> ChatChannelPermissionNodes { get; set; }


        /// <summary>
        /// Returns a ServerPlanetRole using a PlanetRole as a base
        /// </summary>
        public static ServerPlanetRole FromBase(PlanetRole planetrole)
        {
            return MappingManager.Mapper.Map<ServerPlanetRole>(planetrole);
        }

        public List<ServerChatChannelPermissionsNode> GetAllChannelNodes()
        {
            using (ValourDB Context = new ValourDB(ValourDB.DBOptions))
            {
                return Context.ChatChannelPermissionsNodes.Where(x => x.Planet_Id == Planet_Id).ToList();
            }
        }

        public async Task<ChatChannelPermissionsNode> GetChannelNodeAsync(ServerPlanetChatChannel channel, ValourDB db = null)
        {
            bool createdb = false;
            if (db == null)
            {
                db = new ValourDB(ValourDB.DBOptions);
            }

            var res = await db.ChatChannelPermissionsNodes.FirstOrDefaultAsync(x => x.Channel_Id == channel.Id &&
                                                                                    x.Role_Id == Id);

            if (createdb) await db.DisposeAsync();

            return res;
        }

        public async Task<CategoryPermissionsNode> GetCategoryNodeAsync(ServerPlanetCategory category, ValourDB db = null)
        {
            bool createdb = false;
            if (db == null)
            {
                db = new ValourDB(ValourDB.DBOptions);
            }

            var res = await db.CategoryPermissionsNodes.FirstOrDefaultAsync(x => x.Category_Id == category.Id &&
                                                                              x.Role_Id == Id);

            if (createdb) await db.DisposeAsync();

            return res;
        }

        public async Task<PermissionState> GetPermissionStateAsync(Permission permission, ServerPlanetChatChannel channel)
        {
            return await GetPermissionStateAsync(permission, channel.Id);
        }

        public async Task<PermissionState> GetPermissionStateAsync(Permission permission, ulong channel_id)
        {
            using (ValourDB Context = new ValourDB(ValourDB.DBOptions))
            {
                ChatChannelPermissionsNode node = await Context.ChatChannelPermissionsNodes.FirstOrDefaultAsync(x => x.Role_Id == Id && x.Channel_Id == channel_id);
                return node.GetPermissionState(permission);
            }
        }

        /// <summary>
        /// Returns if the role has the permission
        /// </summary>
        /// <param name="permission"></param>
        /// <returns></returns>
        public bool HasPermission(PlanetPermission permission)
        {
            return Permission.HasPermission(Permissions, permission);
        }

        /// <summary>
        /// Tries to delete this role
        /// </summary>
        public async Task<TaskResult<int>> TryDeleteAsync(ServerPlanetMember member, ValourDB db)
        {
            if (member == null)
                return new TaskResult<int>(false, "Member not found", 404);
            
            if (member.Planet_Id != Planet_Id)
                return new TaskResult<int>(false, "Member is of another planet", 403);
            
            if (!await member.HasPermissionAsync(PlanetPermissions.ManageRoles, db))
                return new TaskResult<int>(false, "Member lacks PlanetPermissions.ManageRoles", 403);

            if (await member.GetAuthorityAsync() <= GetAuthority())
                return new TaskResult<int>(false, "Member authority is lower than role authority", 403);
            
            Planet ??= await db.Planets.FindAsync(Planet_Id);

            if (Id == Planet.Default_Role_Id)
                return new TaskResult<int>(false, "Cannot remove default role", 400);
            
            // Remove all members
            var members = db.PlanetRoleMembers.Where(x => x.Role_Id == Id);
            db.PlanetRoleMembers.RemoveRange(members);
            
            // Remove role nodes
            var channelNodes = db.ChatChannelPermissionsNodes.Where(x => x.Role_Id == Id);
            var categoryNodes = db.CategoryPermissionsNodes.Where(x => x.Role_Id == Id);
            
            db.ChatChannelPermissionsNodes.RemoveRange(channelNodes);
            db.CategoryPermissionsNodes.RemoveRange(categoryNodes);
            
            // Remove self
            db.PlanetRoles.Remove(this);

            await db.SaveChangesAsync();
            
            // Notify clients
            PlanetHub.NotifyRoleDeletion(this);

            return new TaskResult<int>(true, "Removed role", 200);
        }

        public async Task<TaskResult<int>> TryUpdateAsync(ServerPlanetMember member, ServerPlanetRole newRole, ValourDB db)
        {
            if (member == null)
                return new TaskResult<int>(false, "Member not found", 403);

            if (member.Planet_Id != Planet_Id)
                return new TaskResult<int>(false, "Member is of another planet", 403);
            
            if (!await member.HasPermissionAsync(PlanetPermissions.ManageRoles, db))
                return new TaskResult<int>(false, "Member lacks PlanetPermissions.ManageRoles", 403);

            if (await member.GetAuthorityAsync() <= GetAuthority())
                return new TaskResult<int>(false, "Member authority is lower than role authority", 403);
            
            if (newRole.Id != Id)
                return new TaskResult<int>(false, "Given role does not match id", 400);

            this.Name = newRole.Name;
            this.Position = newRole.Position;
            this.Permissions = newRole.Permissions;
            this.Color_Red = newRole.Color_Red;
            this.Color_Green = newRole.Color_Green;
            this.Color_Blue = newRole.Color_Blue;
            this.Bold = newRole.Bold;
            this.Italics = newRole.Italics;

            db.PlanetRoles.Update(this);
            await db.SaveChangesAsync();
            
            PlanetHub.NotifyRoleChange(this);

            return new TaskResult<int>(true, "Success", 200);
        }
    }
}
