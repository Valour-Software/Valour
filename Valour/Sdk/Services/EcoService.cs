using System.Web;
using Valour.Sdk.Client;
using Valour.Sdk.ModelLogic;
using Valour.Sdk.Models.Economy;
using Valour.Shared;
using Valour.Shared.Models;
using Valour.Shared.Models.Economy;

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
    
    /// <summary>
    /// Returns a list of global accounts that match the given name. Global accounts are the Valour Credit accounts.
    /// </summary>
    public async Task<EcoGlobalAccountSearchResult> SearchGlobalAccountsAsync(string name)
    {
        var node = await _client.NodeService.GetNodeForPlanetAsync(ISharedPlanet.ValourCentralId);
        return (await node.GetJsonAsync<EcoGlobalAccountSearchResult>($"api/eco/accounts/byname/{HttpUtility.UrlEncode(name)}", true)).Data;
    }
    
    /// <summary>
    /// Returns all eco accounts that the client user has access to.
    /// </summary>
    /// <returns></returns>
    public async Task<TaskResult<List<EcoAccount>>> FetchSelfEcoAccountsAsync()
    {
        return await _client.PrimaryNode.GetJsonAsync<List<EcoAccount>>("api/eco/accounts/self");
    }
    
    public async Task<EcoAccount> GetSelfGlobalAccountAsync()
    {
        var planet = await _client.PlanetService.FetchPlanetAsync(ISharedPlanet.ValourCentralId);
        var account = (await planet.Node.GetJsonAsync<EcoAccount>($"api/eco/accounts/self/global")).Data;
        
        if (account is not null)
            await account.AddToCache(account);

        return account;
    }

    /// <summary>
    /// Returns the eco account with the given id.
    /// The planet will be fetched.
    /// </summary>
    public async ValueTask<EcoAccount> FetchEcoAccountAsync(long id, long planetId, bool skipCache = false)
    {
        var planet = await _client.PlanetService.FetchPlanetAsync(planetId, skipCache);
        return await FetchEcoAccountAsync(id, planet, skipCache);
    }
    
    /// <summary>
    /// Returns the eco account with the given id.
    /// The planet must be provided.
    /// </summary>
    public async ValueTask<EcoAccount> FetchEcoAccountAsync(long id, Planet planet, bool skipCache = false)
    {
        if (!skipCache && _cache.EcoAccounts.TryGet(id, out var cached))
            return cached;
        
        var item = (await planet.Node.GetJsonAsync<EcoAccount>($"api/eco/accounts/{id}")).Data;

        return _cache.Sync(item);
    }
    
    /// <summary>
    /// Returns the transaction with the given id.
    /// </summary>
    public async ValueTask<Transaction> FetchTransactionAsync(string id)
    {
        var item = (await _client.PrimaryNode.GetJsonAsync<Transaction>($"api/eco/transactions/{id}")).Data;
        return item;
    }

    /// <summary>
    /// Sends the given transaction to be processed.
    /// Will fetch the planet.
    /// </summary>
    public async ValueTask<TaskResult<Transaction>> SendTransactionAsync(Transaction trans)
    {
        var planet = await _client.PlanetService.FetchPlanetAsync(trans.PlanetId);
        return await SendTransactionAsync(trans, planet);
    }

    /// <summary>
    /// Sends the given transaction to be processed.
    /// Planet must be provided.
    /// </summary>
    public async ValueTask<TaskResult<Transaction>> SendTransactionAsync(Transaction trans, Planet planet)
    {
        return await planet.Node.PostAsyncWithResponse<Transaction>("api/eco/transactions", trans);
    } 
    
    /// <summary>
    /// Returns the receipt for the given transaction id.
    /// </summary>
    public async ValueTask<EcoReceipt> GetReceiptAsync(string id)
    {
        var item = (await _client.PrimaryNode.GetJsonAsync<EcoReceipt>($"api/eco/transactions/{id}/receipt")).Data;
        return item;
    }
    
    /// <summary>
    /// Returns an engine for querying shared accounts on the given planet.
    /// </summary>
    public ModelQueryEngine<EcoAccount> GetSharedAccountQueryEngine(Planet planet) =>
        new ModelQueryEngine<EcoAccount>(planet.Node, $"api/eco/accounts/planet/{planet.Id}/planet");
    
    /// <summary>
    /// Returns an engine for querying user accounts on the given planet.
    /// </summary>
    public ModelQueryEngine<EcoAccountPlanetMember> GetUserAccountQueryEngine(Planet planet) =>
        new ModelQueryEngine<EcoAccountPlanetMember>(planet.Node, $"api/eco/accounts/planet/{planet.Id}/member"); 
    
    /// <summary>
    /// Returns a paged reader for querying shared accounts on the given planet.
    /// </summary>
    public PagedModelReader<EcoAccount> GetSharedAccountPagedReader(Planet planet, int pageSize = 50) =>
        new PagedModelReader<EcoAccount>(planet.Node, $"api/eco/accounts/planet/{planet.Id}/planet", pageSize);

    /// <summary>
    /// Returns the currency with the given id.
    /// The planet will be fetched.
    /// </summary>
    public async ValueTask<Currency> FetchCurrencyAsync(long id, long planetId, bool skipCache = false)
    {
        var planet = await _client.PlanetService.FetchPlanetAsync(planetId, skipCache);
        return await FetchCurrencyAsync(id, planet, skipCache);
    }

    /// <summary>
    /// Returns the currency with the given id.
    /// Planet must be provided.
    /// </summary>
    public async ValueTask<Currency> FetchCurrencyAsync(long id, Planet planet, bool skipCache = false)
    {
        if (!skipCache && _cache.Currencies.TryGet(id, out var cached))
            return cached;
        
        var item = (await _client.PrimaryNode.GetJsonAsync<Currency>($"api/eco/currencies/{id}")).Data;

        return _cache.Sync(item);
    }
    
    public ValueTask<Currency> FetchGlobalCurrencyAsync() =>
        FetchCurrencyAsync(ISharedCurrency.ValourCreditsId, ISharedPlanet.ValourCentralId);

    /// <summary>
    /// Returns the currency for the given planet.
    /// The planet will be fetched.
    /// </summary>
    public async ValueTask<Currency> FetchCurrencyByPlanetAsync(long planetId, bool skipCache = false)
    {
        var planet = await _client.PlanetService.FetchPlanetAsync(planetId, skipCache);
        return await FetchCurrencyByPlanetAsync(planet);
    }
    
    /// <summary>
    /// Returns the currency for the given planet.
    /// Planet must be provided.
    /// </summary>
    public async ValueTask<Currency> FetchCurrencyByPlanetAsync(Planet planet)
    {
        var item = (await planet.Node.GetJsonAsync<Currency>($"api/eco/currencies/byPlanet/{planet.Id}", true)).Data;

        return _cache.Sync(item);
    }
}