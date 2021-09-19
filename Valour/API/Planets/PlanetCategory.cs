
using Valour.Api.Client;
using Valour.Shared;

namespace Valour.Api.Planets;

/*  Valour - A free and secure chat client
 *  Copyright (C) 2021 Vooper Media LLC
 *  This program is subject to the GNU Affero General Public license
 *  A copy of the license should be included - if not, see <http://www.gnu.org/licenses/>
 */

public class PlanetCategory : Shared.Items.PlanetCategory
{
    /// <summary>
    /// Returns the channel for the given id
    /// </summary>
    public static async Task<TaskResult<PlanetCategory>> FindAsync(ulong id, bool force_refresh = false)
    {
        if (!force_refresh && ValourCache.Categories.ContainsKey(id))
            return new TaskResult<PlanetCategory>(true, "Success: Cached", ValourCache.Categories[id]);

        var getResponse = await ValourClient.GetJsonAsync<PlanetCategory>($"api/category/{id}");

        if (getResponse.Success)
            ValourCache.Categories[id] = getResponse.Data;

        return getResponse;
    }

    /// <summary>
    /// Deletes this channel
    /// </summary>
    public async Task<TaskResult> DeleteAsync() =>
        await ValourClient.DeleteAsync($"api/category/{Id}");

    /// <summary>
    /// Sets the name of this channel
    /// </summary>
    public async Task<TaskResult> SetNameAsync(string name) =>
        await ValourClient.PutAsync($"api/category/{Id}/name", name);

    /// <summary>
    /// Sets the description of this channel
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
    public async Task<TaskResult> SetParentIdAsync(ulong? parent_id) =>
        await ValourClient.PutAsync($"api/category/{Id}/parent_id", parent_id);
}

