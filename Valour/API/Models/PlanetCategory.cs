using Valour.Api.Client;
using Valour.Api.Requests;
using Valour.Shared;
using Valour.Shared.Authorization;
using Valour.Shared.Models;
using Valour.Api.Nodes;
using Valour.Shared.Models;

namespace Valour.Api.Models;

/*  Valour - A free and secure chat client
 *  Copyright (C) 2021 Vooper Media LLC
 *  This program is subject to the GNU Affero General Public license
 *  A copy of the license should be included - if not, see <http://www.gnu.org/licenses/>
 */

public class PlanetCategory : PlanetChannel, ISharedPlanetCategory
{
    #region IPlanetModel implementation

    public override string BaseRoute =>
            $"api/categories";

    #endregion

    public override ChannelType Type => ChannelType.PlanetCategoryChannel;

    public override string GetHumanReadableName() => "Category";

    /// <summary>
    /// Returns the item for the given id
    /// </summary>
    public static async Task<PlanetCategory> FindAsync(long id, long planetId, bool refresh = false)
    {
        if (!refresh)
        {
            var cached = ValourCache.Get<PlanetCategory>(id);
            if (cached is not null)
                return cached;
        }

        var node = await NodeManager.GetNodeForPlanetAsync(planetId);
        var item = (await node.GetJsonAsync<PlanetCategory>($"api/categories/{id}")).Data;

        if (item is not null)
            await item.AddToCache();

        return item;
    }

    public async Task<TaskResult> SetChildOrderAsync(List<long> childIds) =>
        await Node.PostAsync($"{IdRoute}/children/order", childIds);

    public async Task<TaskResult> InsertChild(long childId, int position = -1) =>
        await Node.PostAsync($"{IdRoute}/children/insert/{childId}/{position}", null);

    public static async Task<TaskResult<PlanetCategory>> CreateWithDetails(CreatePlanetCategoryChannelRequest request)
    {
        var node = await NodeManager.GetNodeForPlanetAsync(request.Category.PlanetId);
        return await node.PostAsyncWithResponse<PlanetCategory>($"{request.Category.BaseRoute}/detailed", request);
    }

    // Categories can't really be opened...
    public override Task Open()
        => Task.CompletedTask;

    public override Task Close()
        => Task.CompletedTask;
}

