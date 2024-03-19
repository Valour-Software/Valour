using Microsoft.AspNetCore.Components.Web.Virtualization;
using Valour.Sdk.Client;
using Valour.Sdk.Models;
using Valour.Sdk.Models.Economy;
using Valour.Sdk.Nodes;
using Valour.Shared.Models;

namespace Valour.Client.Providers;

public class EconomyMemberProvider
{
    private readonly string _route;
    private readonly long? _planetId;
    
    public EconomyMemberProvider(string route, long? planetId = null)
    {
        this._route = route;
        this._planetId = planetId;
    }
    
    public async ValueTask<ItemsProviderResult<EcoAccountPlanetMember>> GetItemsAsync(ItemsProviderRequest request)
    {
        var node = _planetId is null ? ValourClient.PrimaryNode : await NodeManager.GetNodeForPlanetAsync(_planetId.Value);

        var result = await node.GetJsonAsync<PagedResponse<EcoAccountPlanetMember>>($"{_route}?skip={request.StartIndex}&take={request.Count}");

        if (!result.Success || result.Data.Items is null)
            return new ItemsProviderResult<EcoAccountPlanetMember>(new List<EcoAccountPlanetMember>(), 0);

        // Sync to cache and use cache instances where available
        foreach (var item in result.Data.Items)
        {
            // This ensures the cache instance is used
            if (item.Member is not null)
            {
                await item.Member.AddToCache(item.Member);
                item.Member = ValourCache.Get<PlanetMember>(item.Member.Id);
            }

            if (item.Account is not null)
            {
                await item.Account.AddToCache(item.Account);
                item.Account = ValourCache.Get<EcoAccount>(item.Account.Id);
            }
            
        }
        
        return new ItemsProviderResult<EcoAccountPlanetMember>(result.Data.Items, result.Data.TotalCount);
    } 
}
