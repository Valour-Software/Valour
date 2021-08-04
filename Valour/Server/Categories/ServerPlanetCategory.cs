using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations.Schema;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Valour.Server.Database;
using Valour.Server.Planets;
using Valour.Server.Roles;
using Valour.Shared;
using Valour.Shared.Categories;
using Valour.Shared.Oauth;
using Valour.Shared.Planets;

namespace Valour.Server.Categories
{
    public class ServerPlanetCategory : PlanetCategory, IServerChannelListItem
    {

        [JsonIgnore]
        [ForeignKey("Planet_Id")]
        public virtual ServerPlanet Planet { get; set; }

        [JsonIgnore]
        public static readonly Regex nameRegex = new Regex(@"^[a-zA-Z0-9 _-]+$");

        public ChannelListItemType ItemType => ChannelListItemType.Category;

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

        public async Task SetNameAsync(string name, ValourDB db = null)
        {
            bool createdb = false;
            if (db == null) { db = new ValourDB(ValourDB.DBOptions); createdb = true; }

            this.Name = name;

            db.PlanetCategories.Update(this);
            await db.SaveChangesAsync();

            if (createdb) { await db.DisposeAsync(); }
        }

        public async Task SetDescriptionAsync(string desc, ValourDB db = null)
        {
            bool createdb = false;
            if (db == null) { db = new ValourDB(ValourDB.DBOptions); createdb = true; }

            this.Description = desc;

            db.PlanetCategories.Update(this);
            await db.SaveChangesAsync();

            if (createdb) { await db.DisposeAsync(); }
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
    }
}
