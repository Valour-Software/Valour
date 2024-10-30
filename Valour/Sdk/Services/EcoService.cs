using System.Web;
using Valour.Sdk.Client;
using Valour.Sdk.ModelLogic;
using Valour.Sdk.Models.Economy;
using Valour.Shared;
using Valour.Shared.Models;

namespace Valour.Sdk.Services;

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
    
    public async Task<EcoGlobalAccountSearchResult> SearchGlobalAccountsAsync(string name)
    {
        var node = await _client.NodeService.GetNodeForPlanetAsync(ISharedPlanet.ValourCentralId);
        return (await node.GetJsonAsync<EcoGlobalAccountSearchResult>($"api/eco/accounts/byname/{HttpUtility.UrlEncode(name)}", true)).Data;
    }
    
    public async Task<TaskResult<List<EcoAccount>>> FetchSelfEcoAccountsAsync()
    {
        return await _client.PrimaryNode.GetJsonAsync<List<EcoAccount>>("api/eco/accounts/self");
    }
    
    public async ValueTask<Transaction> FetchTransactionAsync(string id)
    {
        var item = (await _client.PrimaryNode.GetJsonAsync<Transaction>($"api/eco/transactions/{id}")).Data;
        return item;
    }

    public async ValueTask<TaskResult<Transaction>> SendTransactionAsync(Transaction trans)
    {
        // We do this instead of trans.Node because it's ok to send transactions without a planet being loaded
        // also... transactions aren't a proper model!
        var node = await _client.NodeService.GetNodeForPlanetAsync(trans.PlanetId);
        return await node.PostAsyncWithResponse<Transaction>("api/eco/transactions", trans);
    } 
    
    public async ValueTask<EcoReceipt> GetReceiptAsync(string id)
    {
        var item = (await _client.PrimaryNode.GetJsonAsync<EcoReceipt>($"api/eco/transactions/{id}/receipt")).Data;
        return item;
    }
    
    public ModelQueryEngine<EcoAccount> GetSharedAccountQueryEngine(Planet planet) =>
        new ModelQueryEngine<EcoAccount>(planet.Node, $"api/eco/accounts/planet/{planet.Id}/planet");
    
    public ModelQueryEngine<EcoAccountPlanetMember> GetUserAccountQueryEngine(Planet planet) =>
        new ModelQueryEngine<EcoAccountPlanetMember>(planet.Node, $"api/eco/accounts/planet/{planet.Id}/member"); 
}