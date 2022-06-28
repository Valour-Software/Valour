using System.Text.Json.Serialization;
using Valour.Api.Client;
using Valour.Api.Items.Authorization;
using Valour.Shared;
using Valour.Shared.Items;
using Valour.Shared.Items.Planets.Channels;

namespace Valour.Api.Items.Planets.Channels;

/*  Valour - A free and secure chat client
 *  Copyright (C) 2021 Vooper Media LLC
 *  This program is subject to the GNU Affero General Public license
 *  A copy of the license should be included - if not, see <http://www.gnu.org/licenses/>
 */

public class PlanetCategory : PlanetCategoryBase, IPlanetChannel, ISyncedItem<PlanetCategory>, INodeSpecific
{
    #region Synced Item System

    /// <summary>
    /// Ran when this item is updated
    /// </summary>
    public event Func<int, Task> OnUpdated;

    /// <summary>
    /// Ran when this item is deleted
    /// </summary>
    public event Func<Task> OnDeleted;

    /// <summary>
    /// Run when any of this item type is updated
    /// </summary>
    public static event Func<PlanetCategory, int, Task> OnAnyUpdated;

    /// <summary>
    /// Run when any of this item type is deleted
    /// </summary>
    public static event Func<PlanetCategory, Task> OnAnyDeleted;

    public async Task InvokeAnyUpdated(PlanetCategory updated, int flags)
    {
        if (OnAnyUpdated != null)
            await OnAnyUpdated?.Invoke(updated, flags);
    }

    public async Task InvokeAnyDeleted(PlanetCategory deleted)
    {
        if (OnAnyDeleted != null)
            await OnAnyDeleted?.Invoke(deleted);
    }

    public async Task InvokeUpdated(int flags)
    {
        await OnUpdate(flags);

        if (OnUpdated != null)
            await OnUpdated?.Invoke(flags);
    }

    public async Task InvokeDeleted()
    {
        if (OnDeleted != null)
            await OnDeleted?.Invoke();
    }

    public async Task OnUpdate(int flags)
    {

    }

    #endregion

    /// <summary>
    /// The item type of this item
    /// </summary>
    [JsonPropertyName("ItemType")]
    public override ItemType ItemType => ItemType.Category;

    public string GetItemTypeName() => "Category";

    /// <summary>
    /// Returns the category for the given id
    /// </summary>
    public static async Task<PlanetCategory> FindAsync(ulong id, bool force_refresh = false)
    {
        if (!force_refresh)
        {
            var cached = ValourCache.Get<PlanetCategory>(id);
            if (cached is not null)
                return cached;
        }

        var category = await ValourClient.GetJsonAsync<PlanetCategory>($"api/category/{id}");

        if (category is not null)
            await ValourCache.Put(id, category);

        return category;
    }

    /// <summary>
    /// Returns the planet for this category
    /// </summary>
    public async Task<Planet> GetPlanetAsync() =>
        await Planet.FindAsync(PlanetId);

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
    public async Task<TaskResult> SetParentAsync(PlanetCategory category) =>
        await SetParentIdAsync(category.Id);

    /// <summary>
    /// Sets the parent id of this category
    /// </summary>
    public async Task<TaskResult> SetParentIdAsync(ulong? parentId) =>
        await ValourClient.PutAsync($"api/category/{Id}/parentId", parentId);

    /// <summary>
    /// Returns the planet of this category
    /// </summary>

    public async Task<Planet> GetPlanetAsync(bool force_refresh) =>
        await Planet.FindAsync(PlanetId, force_refresh);

    /// <summary>
    /// Returns the permissions node for the given role id
    /// </summary>
    public async Task<PermissionsNode> GetPermissionsNodeAsync(ulong roleId, bool force_refresh = false) =>
        await GetCategoryPermissionsNodeAsync(roleId, force_refresh);


    /// <summary>
    /// Returns the category permissions node for the given role id
    /// </summary>
    public  async Task<PermissionsNode> GetCategoryPermissionsNodeAsync(ulong roleId, bool force_refresh = false) =>
        await PermissionsNode.FindAsync(Id, roleId, ItemType.Category, force_refresh);

    /// <summary>
    /// Returns the category's default channel permissions node for the given role id
    /// </summary>
    public async Task<PermissionsNode> GetChannelPermissionsNodeAsync(ulong roleId, bool force_refresh = false) =>
        await PermissionsNode.FindAsync(Id, roleId, ItemType.ChatChannel, force_refresh);


}

