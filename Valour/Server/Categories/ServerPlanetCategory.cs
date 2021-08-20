using Microsoft.EntityFrameworkCore;
using Newtonsoft.Json;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations.Schema;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Valour.Server.Database;
using Valour.Server.Planets;
using Valour.Server.Roles;
using Valour.Shared;
using Valour.Shared.Items;
using Valour.Shared.Oauth;
using Valour.Shared.Planets;

namespace Valour.Server.Categories
{
    public class ServerPlanetCategory : IPlanetCategory, IServerChannelListItem
    {

        [JsonIgnore]
        [System.Text.Json.Serialization.JsonIgnore]
        [ForeignKey("Planet_Id")]
        public virtual ServerPlanet Planet { get; set; }

        [JsonIgnore]
        public static readonly Regex nameRegex = new Regex(@"^[a-zA-Z0-9 _-]+$");

        /// <summary>
        /// The id of this category
        /// </summary>
        public ulong Id { get; set; }

        /// <summary>
        /// The name of this category
        /// </summary>
        public string Name { get; set; }

        /// <summary>
        /// The position of this category
        /// </summary>
        public ushort Position { get; set; }

        /// <summary>
        /// The id of the parent of this category (if it exists)
        /// </summary>
        public ulong? Parent_Id { get; set; }

        /// <summary>
        /// The id of the planet containing this category
        /// </summary>
        public ulong Planet_Id { get; set; }

        /// <summary>
        /// The description of this category
        /// </summary>
        public string Description { get; set; }

        /// <summary>
        /// The type of this item
        /// </summary>
        public ItemType ItemType => ItemType.Category;

        /// <summary>
        /// Tries to delete the category while respecting constraints
        /// </summary>
        public async Task<TaskResult> TryDeleteAsync(ValourDB db)
        {
            var planet = await GetPlanetAsync();

            if (await db.PlanetCategories.CountAsync(x => x.Planet_Id == Planet_Id) < 2)
            {
                return new TaskResult(false, "Last category cannot be deleted");
            }

            var childCategoryCount = await db.PlanetCategories.CountAsync(x => x.Parent_Id == Id);
            var childChannelCount = await db.PlanetChatChannels.CountAsync(x => x.Parent_Id == Id);

            if (childCategoryCount != 0 || childChannelCount != 0)
            {
                return new TaskResult(false, "Category must be empty");
            }

            // Remove permission nodes

            db.CategoryPermissionsNodes.RemoveRange(
                db.CategoryPermissionsNodes.Where(x => x.Category_Id == Id)
            );

            // Remove category
            db.PlanetCategories.Remove(
                await db.PlanetCategories.FindAsync(Id)
            );

            // Save changes
            await db.SaveChangesAsync();

            // Notify of update
            await PlanetHub.NotifyCategoryDeletion(this);

            return new TaskResult(true, "Success");
        }

        /// <summary>
        /// Sets the name of this category
        /// </summary>
        public async Task<TaskResult> TrySetNameAsync(string name, ValourDB db)
        {
            TaskResult validName = ValidateName(name);
            if (!validName.Success) return validName;

            this.Name = name;
            db.PlanetCategories.Update(this);
            await db.SaveChangesAsync();

            NotifyClientsChange();

            return new TaskResult(true, "Success");
        }

        /// <summary>
        /// Sets the description of this category
        /// </summary>
        public async Task SetDescriptionAsync(string desc, ValourDB db)
        {
            this.Description = desc;
            db.PlanetCategories.Update(this);
            await db.SaveChangesAsync();

            NotifyClientsChange();
        }

        /// <summary>
        /// Sets the parent of this category
        /// </summary>
        public async Task<TaskResult> TrySetParentAsync(ulong? parent_id, ValourDB db)
        {
            if (parent_id != null)
            {
                var parent = await db.PlanetCategories.FindAsync(parent_id);
                if (parent == null) return new TaskResult(false, "Could not find parent");
                if (parent.Planet_Id != Planet_Id) return new TaskResult(false, "Category belongs to a different planet");
                if (parent.Id == Id) return new TaskResult(false, "Cannot be own parent");

                // TODO: additional loop checking
            }

            this.Parent_Id = parent_id;
            db.PlanetCategories.Update(this);
            await db.SaveChangesAsync();

            NotifyClientsChange();

            return new TaskResult(true, "Success");
        }

        public async Task<Planet> GetPlanetAsync(ValourDB db = null)
        {
            if (Planet != null) return Planet;

            bool createdb = false;
            if (db == null)
            {
                db = new ValourDB(ValourDB.DBOptions);
            }

            Planet = await db.Planets.FindAsync(Planet_Id);

            if (createdb) await db.DisposeAsync();

            return Planet;
        }

        public async Task<bool> HasPermission(ServerPlanetMember member, Permission permission, ValourDB db = null)
        {
            Planet planet = await GetPlanetAsync(db);

            if (planet.Owner_Id == member.User_Id)
            {
                return true;
            }

            var roles = await member.GetRolesAsync(db);

            CategoryPermission catPerm = null;
            ChatChannelPermission chatPerm = null;

            catPerm = permission as CategoryPermission;
            chatPerm = permission as ChatChannelPermission;

            // Starting from the most important role, we stop once we hit the first clear "TRUE/FALSE".
            // If we get an undecided, we continue to the next role down
            foreach (var role in roles)
            {
                var node = await ServerPlanetRole.FromBase(role).GetCategoryNodeAsync(this);

                // If we are dealing with the default role and the behavior is undefined, we fall back to the default permissions
                if (node == null)
                {
                    if (role.Id == planet.Default_Role_Id)
                    {
                        if (catPerm != null)
                        {
                            return Permission.HasPermission(CategoryPermissions.Default, permission);
                        }
                        else if (chatPerm != null)
                        {
                            return Permission.HasPermission(ChatChannelPermissions.Default, permission);
                        }
                    }

                    continue;
                }

                PermissionState state = PermissionState.Undefined;

                if (catPerm != null)
                {
                    state = node.GetPermissionState(permission);
                }
                else if (chatPerm != null)
                {
                    state = node.GetChatChannelPermissionState(permission);
                }

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

        public void NotifyClientsChange()
        {
            PlanetHub.NotifyCategoryChange(this);
        }

        /// <summary>
        /// Validates that a given name is allowable for a server
        /// </summary>
        public static TaskResult ValidateName(string name)
        {
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

        public static async Task<ServerPlanetCategory> FindAsync(ulong id, ValourDB db)
        {
            return await db.PlanetCategories.FindAsync(id);
        }
    }
}
