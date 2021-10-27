
using Valour.Api.Authorization.Roles;
using Valour.Api.Client;
using Valour.Shared;
using Valour.Shared.Items;

namespace Valour.Api.Planets;

/*  Valour - A free and secure chat client
 *  Copyright (C) 2021 Vooper Media LLC
 *  This program is subject to the GNU Affero General Public license
 *  A copy of the license should be included - if not, see <http://www.gnu.org/licenses/>
 */

public class Category : Shared.Items.PlanetCategory<Category>, IPlanetListItem
{
    public string GetItemTypeName() => "Category";

    /// <summary>
    /// Returns the category for the given id
    /// </summary>
    public static async Task<Category> FindAsync(ulong id, bool force_refresh = false)
    {
        if (!force_refresh)
        {
            var cached = ValourCache.Get<Category>(id);
            if (cached is not null)
                return cached;
        }

        var category = await ValourClient.GetJsonAsync<Category>($"api/category/{id}");

        if (category is not null)
            await ValourCache.Put(id, category);

        return category;
    }

    /// <summary>
    /// Returns the planet for this category
    /// </summary>
    public async Task<Planet> GetPlanetAsync() =>
        await Planet.FindAsync(Planet_Id);

    /// <summary>
    /// Deletes this category
    /// </summary>
    public async Task<TaskResult> DeleteAsync() =>
        await ValourClient.DeleteAsync($"api/category/{Id}");

    /// <summary>
    /// Sets the name of this category
    /// </summary>
    public async Task<TaskResult> SetNameAsync(string name) =>
        await ValourClient.PutAsync($"api/category/{Id}/name", name);

    /// <summary>
    /// Sets the description of this category
    /// </summary>
    public async Task<TaskResult> SetDescriptionAsync(string desc) =>
        await ValourClient.PutAsync($"api/category/{Id}/description", desc);

    /// <summary>
    /// Sets the parent of this category
    /// </summary>
    public async Task<TaskResult> SetParentAsync(Category category) =>
        await SetParentIdAsync(category.Id);

    /// <summary>
    /// Sets the parent id of this category
    /// </summary>
    public async Task<TaskResult> SetParentIdAsync(ulong? parent_id) =>
        await ValourClient.PutAsync($"api/category/{Id}/parent_id", parent_id);

    /// <summary>
    /// Returns the planet of this category
    /// </summary>

    public async Task<Planet> GetPlanetAsync(bool force_refresh) =>
        await Planet.FindAsync(Planet_Id, force_refresh);

    /// <summary>
    /// Returns the permissions node for the given role id
    /// </summary>
    public async Task<PermissionsNode> GetPermissionsNodeAsync(ulong role_id, bool force_refresh = false) =>
        await GetCategoryPermissionsNodeAsync(role_id, force_refresh);


    /// <summary>
    /// Returns the category permissions node for the given role id
    /// </summary>
    public async Task<PermissionsNode> GetCategoryPermissionsNodeAsync(ulong role_id, bool force_refresh = false) =>
        await PermissionsNode.FindAsync(Id, role_id, ItemType.Category, force_refresh);

    /// <summary>
    /// Returns the category's default channel permissions node for the given role id
    /// </summary>
    public async Task<PermissionsNode> GetChannelPermissionsNodeAsync(ulong role_id, bool force_refresh = false) =>
        await PermissionsNode.FindAsync(Id, role_id, ItemType.Channel, force_refresh);


}

