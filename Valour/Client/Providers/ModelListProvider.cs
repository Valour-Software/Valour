using Microsoft.AspNetCore.Components.Web.Virtualization;
using Valour.Sdk.Client;
using Valour.Sdk.Nodes;
using Valour.Shared.Models;

namespace Valour.Client.Providers;

public class ModelListProvider<T>
{
    private readonly string _route;
    private readonly long? _planetId;
    
    
    public ModelListProvider(string route, long? planetId = null)
    {
        this._route = route;
        this._planetId = planetId;
    }
    
    public async ValueTask<ItemsProviderResult<T>> GetItemsAsync(ItemsProviderRequest request)
    {
        var node = _planetId is null ? ValourClient.PrimaryNode : await NodeManager.GetNodeForPlanetAsync(_planetId.Value);

        var result = await node.GetJsonAsync<PagedResponse<T>>($"{_route}?skip={request.StartIndex}&take={request.Count}");

        if (!result.Success || result.Data.Items is null)
            return new ItemsProviderResult<T>(new List<T>(), 0);
        
        return new ItemsProviderResult<T>(result.Data.Items, result.Data.TotalCount);
    } 
}