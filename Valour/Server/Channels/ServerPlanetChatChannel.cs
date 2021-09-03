using AutoMapper;
using Microsoft.EntityFrameworkCore;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Valour.Server.Database;
using Valour.Shared.Oauth;
using Valour.Shared.Roles;
using System.Text.Json.Serialization;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text.RegularExpressions;
using Valour.Shared;
using Valour.Server.Categories;
using Valour.Shared.Items;

namespace Valour.Server.Planets
{
    /*  Valour - A free and secure chat client
     *  Copyright (C) 2021 Vooper Media LLC
     *  This program is subject to the GNU Affero General Public license
     *  A copy of the license should be included - if not, see <http://www.gnu.org/licenses/>
     */

    /// <summary>
    /// This class exists to add server funtionality to the Planet Chat Channel
    /// class. It does not, and should not, have any extra fields or properties.
    /// Just helper methods.
    /// </summary>
    public class ServerPlanetChatChannel : IPlanetChatChannel, IServerChannelListItem
    {

        [ForeignKey("Planet_Id")]
        [JsonIgnore]
        public virtual ServerPlanet Planet { get; set; }

        [ForeignKey("Parent_Id")]
        [JsonIgnore]
        public virtual ServerPlanetCategory Parent { get; set; }

        /// <summary>
        /// The id of this channel
        /// </summary>
        [JsonPropertyName("Id")]
        public ulong Id { get; set; }

        /// <summary>
        /// The name of this channel
        /// </summary>
        [JsonPropertyName("Name")]
        public string Name { get; set; }

        /// <summary>
        /// The number of messages within this channel
        /// </summary>
        [JsonPropertyName("Message_Count")]
        public ulong Message_Count { get; set; }

        /// <summary>
        /// True of this channel inherits permissions from its category
        /// </summary>
        [JsonPropertyName("Inherits_Perms")]
        public bool Inherits_Perms { get; set; }

        /// <summary>
        /// The position of this channel
        /// </summary>
        [JsonPropertyName("Position")]
        public ushort Position { get; set; }

        /// <summary>
        /// The id of the parent category of this channel
        /// </summary>
        [JsonPropertyName("Parent_Id")]
        public ulong? Parent_Id { get; set; }

        /// <summary>
        /// The id of the planet this channel belongs to
        /// </summary>
        [JsonPropertyName("Planet_Id")]
        public ulong Planet_Id { get; set; }

        /// <summary>
        /// The description of this channel
        /// </summary>
        [JsonPropertyName("Description")]
        public string Description { get; set; }

        [JsonPropertyName("ItemType")]
        public ItemType ItemType => ItemType.Channel;

        /// <summary>
        /// The regex used for name validation
        /// </summary>
        public static readonly Regex nameRegex = new Regex(@"^[a-zA-Z0-9 _-]+$");

        /// <summary>
        /// Deletes this channel
        /// </summary>
        public async Task<TaskResult<int>> TryDeleteAsync(ServerPlanetMember member, ValourDB db)
        {
            Planet ??= await GetPlanetAsync(db);

            if (Id == Planet.Main_Channel_Id)
                return new TaskResult<int>(false, $"Cannot delete main channel", 400);

            if (member == null) 
                return new TaskResult<int>(false, "Member not found", 403);

            if (!await HasPermission(member, ChatChannelPermissions.View, db)) 
                return new TaskResult<int>(false, "Member lacks ChatChannelPermissions.View", 403);

            if (!await HasPermission(member, ChatChannelPermissions.ManageChannel, db)) 
                return new TaskResult<int>(false, "Member lacks ChatChannelPermissions.ManageChannel", 403);

            // Remove permission nodes
            db.ChatChannelPermissionsNodes.RemoveRange(
                db.ChatChannelPermissionsNodes.Where(x => x.Channel_Id == Id)
            );

            // Remove messages
            db.PlanetMessages.RemoveRange(
                db.PlanetMessages.Where(x => x.Channel_Id == Id)
            );

            // Remove channel
            db.PlanetChatChannels.Remove(
                await db.PlanetChatChannels.FirstOrDefaultAsync(x => x.Id == Id)
            );

            // Save changes
            await db.SaveChangesAsync();

            // Notify channel deletion
            await PlanetHub.NotifyChatChannelDeletion(this);

            return new TaskResult<int>(true, "Success", 200);
        }

        /// <summary>
        /// Returns the planet this channel belongs to
        /// </summary>
        public async Task<ServerPlanet> GetPlanetAsync(ValourDB db)
        {
            Planet ??= await db.Planets.FindAsync(Planet_Id);
            return Planet;
        }

        /// <summary>
        /// Returns the parent category of this channel
        /// </summary>
        public async Task<ServerPlanetCategory> GetParentAsync(ValourDB db)
        {
            Parent ??= await db.PlanetCategories.FindAsync(Parent_Id);
            return Parent;
        }

        /// <summary>
        /// Returns if a given member has a channel permission
        /// </summary>
        public async Task<bool> HasPermission(ServerPlanetMember member, ChatChannelPermission permission, ValourDB db)
        {
            Planet ??= await GetPlanetAsync(db);

            if (Planet.Owner_Id == member.User_Id)
                return true;

            // If true, we just ask the category
            if (Inherits_Perms)
            {
                if (Parent == null)
                {
                    Parent = await GetParentAsync(db);
                }

                return await Parent.HasPermission(member, permission);
            }


            // Load permission data
            await db.Entry(member).Collection(x => x.RoleMembership)
                                  .Query()
                                  .Where(x => x.Planet_Id == Planet.Id)
                                  .Include(x => x.Role)
                                  .ThenInclude(x => x.ChatChannelPermissionNodes.Where(x => x.Channel_Id == Id))
                                  .LoadAsync();

            // Starting from the most important role, we stop once we hit the first clear "TRUE/FALSE".
            // If we get an undecided, we continue to the next role down
            foreach (var roleMembership in member.RoleMembership)
            {
                var role = roleMembership.Role;
                ChatChannelPermissionsNode node = role.ChatChannelPermissionNodes.FirstOrDefault();

                // If we are dealing with the default role and the behavior is undefined, we fall back to the default permissions
                if (node == null)
                {
                    if (role.Id == Planet.Default_Role_Id)
                    {
                        return Permission.HasPermission(ChatChannelPermissions.Default, permission);
                    }

                    continue;
                }

                PermissionState state = node.GetPermissionState(permission);

                if (state == PermissionState.Undefined)
                {
                    continue;
                }
                else if (state == PermissionState.True)
                {
                    return true;
                }
                else
                {
                    return false;
                }

            }

            // No roles ever defined behavior: resort to false.
            return false;
        }

        /// <summary>
        /// Validates that a given name is allowable for a channel
        /// </summary>
        public static TaskResult ValidateName(string name)
        {
            if (name.Length > 32)
                return new TaskResult(false, "Channel names must be 32 characters or less.");

            if (!nameRegex.IsMatch(name))
                return new TaskResult(false, "Channel names may only include letters, numbers, dashes, and underscores.");

            return new TaskResult(true, "The given name is valid.");
        }

        /// <summary>
        /// Sets the name of this channel
        /// </summary>
        public async Task<TaskResult> TrySetNameAsync(string name, ValourDB db)
        {
            TaskResult validName = ValidateName(name);
            if (!validName.Success) return validName;

            this.Name = name;
            db.PlanetChatChannels.Update(this);
            await db.SaveChangesAsync();

            NotifyClientsChange();

            return new TaskResult(true, "Success");
        }

        /// <summary>
        /// Sets the description of this channel
        /// </summary>
        public async Task SetDescriptionAsync(string desc, ValourDB db)
        {
            this.Description = desc;
            db.PlanetChatChannels.Update(this);
            await db.SaveChangesAsync();

            NotifyClientsChange();
        }

        /// <summary>
        /// Sets the parent of this channel
        /// </summary>
        public async Task SetParentAsync(ulong parent_id, ValourDB db)
        {
            this.Parent_Id = parent_id;
            db.PlanetChatChannels.Update(this);
            await db.SaveChangesAsync();

            NotifyClientsChange();
        }

        /// <summary>
        /// Sets the permissions inherit mode of this channel
        /// </summary>
        public async Task SetInheritsPermsAsync(bool inherits_perms, ValourDB db)
        {
            this.Inherits_Perms = inherits_perms;
            db.PlanetChatChannels.Update(this);
            await db.SaveChangesAsync();

            NotifyClientsChange();
        }

        /// <summary>
        /// Returns all members who can see this channel
        /// </summary>
        public async Task<List<ServerPlanetMember>> GetChannelMembersAsync(ValourDB db = null)
        {
            List<ServerPlanetMember> members = new List<ServerPlanetMember>(); 

            bool createdb = false;
            if (db == null) { db = new ValourDB(ValourDB.DBOptions); createdb = true; }

            var planetMembers = db.PlanetMembers.Include(x => x.RoleMembership).Where(x => x.Planet_Id == Planet_Id);

            foreach (var member in planetMembers)
            {
                if (await HasPermission(member, ChatChannelPermissions.View, db))
                {
                    members.Add(member);
                }
            }

            if (createdb) { await db.DisposeAsync(); }

            return members;
        }

        /// <summary>
        /// Notifies all clients that this channel has changed
        /// </summary>
        public void NotifyClientsChange()
        {
            PlanetHub.NotifyChatChannelChange(this);
        }

        public static async Task<ServerPlanetChatChannel> FindAsync(ulong id, ValourDB db)
        {
            return await db.PlanetChatChannels.FindAsync(id);
        }
    }
}
