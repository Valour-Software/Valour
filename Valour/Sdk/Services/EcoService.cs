using System.Web;
using Valour.Sdk.Client;
using Valour.Sdk.Models.Economy;
using Valour.Shared;
using Valour.Shared.Models;

namespace Valour.SDK.Services;

public class EcoService : ServiceBase
{
    private static readonly LogOptions LogOptions = new(
        "EcoService",
        "#0083ab",
        "#ab0055",
        "#ab8900"
    );
    
    private readonly ValourClient _client;
    private readonly CacheService _cache;
    
    public EcoService(ValourClient client)
    {
        _client = client;
        _cache = client.Cache;
        SetupLogging(client.Logger, LogOptions);
    }
    
    public async Task<EcoGlobalAccountSearchResult> FindGlobalIdByNameAsync(string name)
    {
        var node = await _client.NodeService.GetNodeForPlanetAsync(ISharedPlanet.ValourCentralId);
        return (await node.GetJsonAsync<EcoGlobalAccountSearchResult>($"api/eco/accounts/byname/{HttpUtility.UrlEncode(name)}", true)).Data;
    }
    
    public async Task<TaskResult<List<EcoAccount>>> FetchSelfEcoAccountsAsync()
    {
        return await _client.PrimaryNode.GetJsonAsync<List<EcoAccount>>("api/eco/accounts/self");
    }
    
    public async Task<ItemsProviderResult<EcoAccountPlanetMember>> GetItemsAsync(ItemsProviderRequest request)
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
                await item.Member.AddToCacheAsync(item.Member);
                item.Member = ModelCache<,>.Get<PlanetMember>(item.Member.Id);
            }

            if (item.Account is not null)
            {
                await item.Account.AddToCache(item.Account);
                item.Account = ModelCache<,>.Get<EcoAccount>(item.Account.Id);
            }
            
        }
        
        return new ItemsProviderResult<EcoAccountPlanetMember>(result.Data.Items, result.Data.TotalCount);
    }
    
    public static async ValueTask<Transaction> FindAsync(string id)
    {
        var item = (await ValourClient.PrimaryNode.GetJsonAsync<Transaction>($"api/eco/transactions/{id}")).Data;
        return item;
    }

    public static async ValueTask<TaskResult<Transaction>> SendTransactionAsync(Transaction trans)
    {
        var node = await NodeManager.GetNodeForPlanetAsync(trans.PlanetId);
        return await node.PostAsyncWithResponse<Transaction>("api/eco/transactions", trans);
    } 
    
    public static async ValueTask<EcoReceipt> GetReceiptAsync(string id)
    {
        var item = (await ValourClient.PrimaryNode.GetJsonAsync<EcoReceipt>($"api/eco/transactions/{id}/receipt")).Data;
        return item;
    }
}