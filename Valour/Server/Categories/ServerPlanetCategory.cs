using Microsoft.EntityFrameworkCore;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations.Schema;
using System.Linq;
using System.Text.Json.Serialization;
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
    public class ServerPlanetCategory : PlanetCategory<ServerPlanetCategory>, IServerChannelListItem
    {

        [JsonIgnore]
        [ForeignKey("Planet_Id")]
        public virtual ServerPlanet Planet { get; set; }

        [JsonIgnore]
        public static readonly Regex nameRegex = new Regex(@"^[a-zA-Z0-9 _-]+$");

        /// <summary>
        /// The id of this category
        /// </summary>
        [JsonPropertyName("Id")]
        public ulong Id { get; set; }

        /// <summary>
        /// The name of this category
        /// </summary>
        [JsonPropertyName("Name")]
        public string Name { get; set; }

        /// <summary>
        /// The position of this category
        /// </summary>
        [JsonPropertyName("Position")]
        public ushort Position { get; set; }

        /// <summary>
        /// The id of the parent of this category (if it exists)
        /// </summary>
        [JsonPropertyName("Parent_Id")]
        public ulong? Parent_Id { get; set; }

        /// <summary>
        /// The id of the planet containing this category
        /// </summary>
        [JsonPropertyName("Planet_Id")]
        public ulong Planet_Id { get; set; }

        /// <summary>
        /// The description of this category
        /// </summary>
        [JsonPropertyName("Description")]
        public string Description { get; set; }

        /// <summary>
        /// The type of this item
        /// </summary>
        [NotMapped]
        [JsonPropertyName("ItemType")]
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

            db.PermissionsNodes.RemoveRange(
                db.PermissionsNodes.Where(x => x.Target_Id == Id && x.Target_Type == ItemType.Category)
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
        public async Task<TaskResult<int>> TrySetParentAsync(ServerPlanetMember member, ulong? parent_id, int position, ValourDB db)
        {
            if (member == null)
                return new TaskResult<int>(false, "Member not found", 403);
            if (!await HasPermission(member, CategoryPermissions.ManageCategory, db))
                return new TaskResult<int>(false, "Member lacks CategoryPermissions.ManageCategory", 403);

            if (parent_id != null)
            {
                var parent = await db.PlanetCategories.FindAsync(parent_id);
                if (parent == null) return new TaskResult<int>(false, "Could not find parent", 404);
                if (parent.Planet_Id != Planet_Id) return new TaskResult<int>(false, "Category belongs to a different planet", 400);
                if (parent.Id == Id) return new TaskResult<int>(false, "Cannot be own parent", 400);

                if (position == -1)
                {
                    var o_cats = await db.PlanetCategories.CountAsync(x => x.Parent_Id == parent_id);
                    var o_chans = await db.PlanetChatChannels.CountAsync(x => x.Parent_Id == parent_id);
                    this.Position = (ushort)(o_cats + o_chans);
                }
                else
                {
                    this.Position = (ushort)position;
                }

                // TODO: additional loop checking
            }
            else
            {
                if (position == -1)
                {
                    var o_cats = await db.PlanetCategories.CountAsync(x => x.Planet_Id == Planet_Id && x.Parent_Id == null);
                    this.Position = (ushort)o_cats;
                }
                else
                {
                    this.Position = (ushort)position;
                }
            }

            this.Parent_Id = parent_id;
            db.PlanetCategories.Update(this);
            await db.SaveChangesAsync();

            NotifyClientsChange();

            return new TaskResult<int>(true, "Success", 200);
        }

        public async Task<ServerPlanet> GetPlanetAsync(ValourDB db = null)
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

        public async Task<bool> HasPermission(ServerPlanetMember member, Permission permission, ValourDB db)
        {
            ServerPlanet planet = await GetPlanetAsync(db);

            if (planet.Owner_Id == member.User_Id)
            {
                return true;
            }

            var roles = await member.GetRolesAsync(db);

            // Starting from the most important role, we stop once we hit the first clear "TRUE/FALSE".
            // If we get an undecided, we continue to the next role down
            foreach (var role in roles)
            {
                var node = await role.GetCategoryNodeAsync(this, db);

                // If we are dealing with the default role and the behavior is undefined, we fall back to the default permissions
                if (node == null)
                {
                    if (role.Id == planet.Default_Role_Id)
                    {
                        return Permission.HasPermission(CategoryPermissions.Default, permission);
                    }

                    continue;
                }

                PermissionState state = PermissionState.Undefined;

                state = node.GetPermissionState(permission);

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
