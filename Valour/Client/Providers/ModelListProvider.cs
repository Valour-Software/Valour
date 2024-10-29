using Microsoft.AspNetCore.Components.Web.Virtualization;
using Valour.Sdk.Client;
using Valour.Sdk.Nodes;
using Valour.Shared.Models;

namespace Valour.Client.Providers;

public class ModelListProvider<T>
{
    private readonly string _route;
    private readonly Node _node;
    
    public ModelListProvider(Node node, string route)
    {
        this._route = route;
        this._node = node;
    }
    
    public async ValueTask<ItemsProviderResult<T>> GetItemsAsync(ItemsProviderRequest request)
    {
        var result = await _node.GetJsonAsync<PagedResponse<T>>($"{_route}?skip={request.StartIndex}&take={request.Count}");

        if (!result.Success || result.Data.Items is null)
            return new ItemsProviderResult<T>(new List<T>(), 0);
        
        return new ItemsProviderResult<T>(result.Data.Items, result.Data.TotalCount);
    } 
}