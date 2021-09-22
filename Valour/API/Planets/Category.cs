
using Valour.Api.Authorization.Roles;
using Valour.Api.Client;
using Valour.Shared;

namespace Valour.Api.Planets;

/*  Valour - A free and secure chat client
 *  Copyright (C) 2021 Vooper Media LLC
 *  This program is subject to the GNU Affero General Public license
 *  A copy of the license should be included - if not, see <http://www.gnu.org/licenses/>
 */

public class Category : Shared.Items.PlanetCategory
{
    /// <summary>
    /// Returns the category for the given id
    /// </summary>
    public static async Task<TaskResult<Category>> FindAsync(ulong id, bool force_refresh = false)
    {
        if (!force_refresh)
        {
            var cached = ValourCache.Get<Category>(id);
            if (cached is not null)
                return new TaskResult<Category>(true, "Success: Cached", cached);
        }

        var getResponse = await ValourClient.GetJsonAsync<Category>($"api/category/{id}");

        if (getResponse.Success)
            ValourCache.Put(id, getResponse.Data);

        return getResponse;
    }

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

    public async Task<TaskResult<Planet>> GetPlanetAsync(bool force_refresh) =>
        await Planet.FindAsync(Planet_Id, force_refresh);

    /// <summary>
    /// Returns the permissions node for the given role id
    /// </summary>
    public async Task<TaskResult<Shared.Roles.PermissionsNode>> GetPermissionsNodeAsync(ulong role_id, bool force_refresh = false)
    {
        var res = await GetCategoryPermissionsNodeAsync(role_id, force_refresh);
        return new TaskResult<Shared.Roles.PermissionsNode>(res.Success, res.Message, res.Data);
    }

    /// <summary>
    /// Returns the category permissions node for the given role id
    /// </summary>
    public async Task<TaskResult<CategoryPermissionsNode>> GetCategoryPermissionsNodeAsync(ulong role_id, bool force_refresh = false) =>
        await CategoryPermissionsNode.FindAsync(Id, role_id, force_refresh);
}

