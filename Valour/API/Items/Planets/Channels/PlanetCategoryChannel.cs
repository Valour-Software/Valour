using Valour.Api.Client;
using Valour.Api.Items.Authorization;
using Valour.Api.Requests;
using Valour.Shared;
using Valour.Shared.Items.Authorization;
using Valour.Shared.Items.Planets.Channels;

namespace Valour.Api.Items.Planets.Channels;

/*  Valour - A free and secure chat client
 *  Copyright (C) 2021 Vooper Media LLC
 *  This program is subject to the GNU Affero General Public license
 *  A copy of the license should be included - if not, see <http://www.gnu.org/licenses/>
 */

public class PlanetCategoryChannel : PlanetChannel, ISharedPlanetCategoryChannel
{
    /// <summary>
    /// True if this category inherits permissions from its parent
    /// </summary>
    public bool InheritsPerms { get; set; }

    public override string GetHumanReadableName() => "Category";

    /// <summary>
    /// Returns the item for the given id
    /// </summary>
    public static async Task<PlanetCategoryChannel> FindAsync(long id, long planetId, bool refresh = false)
    {
        if (!refresh)
        {
            var cached = ValourCache.Get<PlanetCategoryChannel>(id);
            if (cached is not null)
                return cached;
        }

        var item = await ValourClient.GetJsonAsync<PlanetCategoryChannel>($"api/{nameof(Planet)}/{planetId}/{nameof(PlanetCategoryChannel)}/{id}");

        if (item is not null)
            await item.AddToCache();

        return item;
    }

    /// <summary>
    /// Returns the permissions node for the given role id
    /// </summary>
    public override async Task<PermissionsNode> GetPermissionsNodeAsync(long roleId, bool force_refresh = false) =>
        await GetCategoryPermissionsNodeAsync(roleId, force_refresh);


    /// <summary>
    /// Returns the category permissions node for the given role id
    /// </summary>
    public  async Task<PermissionsNode> GetCategoryPermissionsNodeAsync(long roleId, bool force_refresh = false) =>
        await PermissionsNode.FindAsync(Id, roleId, PermissionsTarget.PlanetCategoryChannel, force_refresh);

    /// <summary>
    /// Returns the category's default channel permissions node for the given role id
    /// </summary>
    public async Task<PermissionsNode> GetChannelPermissionsNodeAsync(long roleId, bool force_refresh = false) =>
        await PermissionsNode.FindAsync(Id, roleId, PermissionsTarget.PlanetChatChannel, force_refresh);

    public static async Task<TaskResult<PlanetCategoryChannel>> CreateWithDetails(CreatePlanetCategoryChannelRequest request)
    {
        return await ValourClient.PostAsyncWithResponse<PlanetCategoryChannel>($"{request.Category.BaseRoute}/detailed", request);
    }
}

