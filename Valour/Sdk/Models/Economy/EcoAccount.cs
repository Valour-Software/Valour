using System.Web;
using Valour.Sdk.Client;
using Valour.Sdk.ModelLogic;
using Valour.Shared.Models;
using Valour.Shared.Models.Economy;

namespace Valour.Sdk.Models.Economy;

/// <summary>
/// An account is an economic storage system for planets and users to hold
/// and transact currencies
/// 
/// It should be noted that accounts do NOT allow a negative balance.
/// Communities who wish to represent debts should track them with
/// their own integrations, as debt allows the money cap to grow
/// in unexpected ways.
/// 
/// Also note that accounts internally handle rounding issues by forcing all
/// transactions to the number of decimal places defined in the currency.
/// If you have a currency with two decimal places, and you attempt to 
/// subtract 0.333... from cash, it will end up subtracting 0.33.
/// </summary>
public class EcoAccount : ClientModel<EcoAccount, long>, ISharedEcoAccount
{
    public override string BaseRoute => "api/eco/accounts";

    /// <summary>
    /// The name of the account
    /// </summary>
    public string Name { get; set; }
    
    /// <summary>
    /// The type of account this represents
    /// </summary>
    public AccountType AccountType { get; set; }

    /// <summary>
    /// The id of the user who opened this account
    /// This will always be set, even for planet accounts
    /// </summary>
    public long UserId { get; set; }

    /// <summary>
    /// The id of the planet this economy account belongs to
    /// This will always be set
    /// </summary>
    public long PlanetId { get; set; }
    
    /// <summary>
    /// The member id of the planet member this account belongs to
    /// </summary>
    public long? PlanetMemberId { get; set; }

    /// <summary>
    /// The id of the currency this account is using
    /// </summary>
    public long CurrencyId { get; set; }

    /// <summary>
    /// The value of the balance of this account
    /// This should *not* be used in code. Use the service's GetBalance instead.
    /// This is just for mapping to the database.
    /// </summary>
    public decimal BalanceValue { get; set; }
    
    public static async ValueTask<EcoAccount> FindAsync(long id, long planetId, bool refresh = false)
    {
        if (!refresh)
        {
            var cached = ModelCache<,>.Get<EcoAccount>(id);
            if (cached != null)
                return cached;
        }

        var node = await NodeManager.GetNodeForPlanetAsync(planetId);
        var item = (await node.GetJsonAsync<EcoAccount>($"api/eco/accounts/{id}")).Data;

        if (item is not null)
            await item.AddToCache(item);

        return item;
    }
    
    public static async ValueTask<EcoGlobalAccountSearchResult> FindGlobalIdByNameAsync(string name)
    {
        var node = await NodeManager.GetNodeForPlanetAsync(ISharedPlanet.ValourCentralId);
        return (await node.GetJsonAsync<EcoGlobalAccountSearchResult>($"api/eco/accounts/byname/{HttpUtility.UrlEncode(name)}", true)).Data;
    }

    /// <summary>
    /// Returns all planet accounts for the given planet id
    /// </summary>
    public static async Task<PagedResponse<EcoAccount>> GetPlanetPlanetAccountsAsync(long planetId, int skip = 0, int take = 50)
    {
        if (take > 50)
            take = 50;
        
        var node = await NodeManager.GetNodeForPlanetAsync(planetId);
        var accounts = (await node.GetJsonAsync<PagedResponse<EcoAccount>>($"api/eco/accounts/planet/{planetId}/planet?skip={skip}&take={take}")).Data;

        if (accounts.Items is not null)
        {
            foreach (var account in accounts.Items)
            {
                await account.AddToCache(account);
            }
        }

        return accounts;
    }
    
    /// <summary>
    /// Returns all user accounts for the given planet id
    /// </summary>
    public static async Task<PagedResponse<EcoAccount>> GetPlanetUserAccountsAsync(long planetId, int skip = 0, int take = 50)
    {
        if (take > 50)
            take = 50;
        
        var node = await NodeManager.GetNodeForPlanetAsync(planetId);
        var accounts = (await node.GetJsonAsync<PagedResponse<EcoAccount>>($"api/eco/accounts/planet/{planetId}/user?skip=")).Data;

        if (accounts.Items is not null)
        {
            foreach (var account in accounts.Items)
            {
                await account.AddToCache(account);
            }
        }

        return accounts;
    }
    
    /// <summary>
    /// Returns all user accounts for the given planet id
    /// </summary>
    public static async Task<PagedResponse<EcoAccountPlanetMember>> GetPlanetUserAccountsWithMemberAsync(long planetId)
    {
        var node = await NodeManager.GetNodeForPlanetAsync(planetId);
        var results = (await node.GetJsonAsync<PagedResponse<EcoAccountPlanetMember>>($"api/eco/accounts/planet/{planetId}/member")).Data;

        if (results.Items is not null)
        {
            foreach (var account in results.Items)
            {
                await account.Account.AddToCache(account.Account);
                await account.Member.AddToCacheAsync(account.Member);
            }
        }

        return results;
    }

    /// <summary>
    /// Returns all accounts the user can send to for a given planet id
    /// </summary>
    public static async Task<List<EcoAccountSearchResult>> GetPlanetAccountsCanSendAsync(long planetId, long accountId, string filter = "")
    {
        var node = await NodeManager.GetNodeForPlanetAsync(planetId);

        var request = new EcoPlanetAccountSearchRequest()
        {
            PlanetId = planetId,
            AccountId = accountId,
            Filter = filter
        };
        
        var response = (await node.PostAsyncWithResponse<List<EcoAccountSearchResult>>($"api/eco/accounts/planet/canSend", request)).Data;
        
        if (response is not null)
        {
            foreach (var accountData in response)
            {
                await accountData.Account.AddToCache(accountData.Account);
            }
        }

        return response;
    }

    public static async Task<EcoAccount> GetSelfGlobalAccountAsync()
    {
        var node = await NodeManager.GetNodeForPlanetAsync(ISharedPlanet.ValourCentralId);
        var account = (await node.GetJsonAsync<EcoAccount>($"api/eco/accounts/self/global")).Data;
        
        if (account is not null)
            await account.AddToCache(account);

        return account;
    }
}
