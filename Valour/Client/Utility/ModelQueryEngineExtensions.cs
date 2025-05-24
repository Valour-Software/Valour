using Microsoft.AspNetCore.Components.Web.Virtualization;
using Valour.Sdk.ModelLogic;

namespace Valour.Client.Utility;

public static class ModelQueryEngineExtensions
{
    public static async ValueTask<ItemsProviderResult<TModel>> GetVirtualizedItemsAsync<TModel>(this IModelQueryEngine<TModel> engine, ItemsProviderRequest request)
        where TModel : ClientModel<TModel>
    {
        var queryData = await engine.GetItemsAsync(request.StartIndex, request.Count);
        return new ItemsProviderResult<TModel>(queryData.Items, queryData.TotalCount);
    }
}