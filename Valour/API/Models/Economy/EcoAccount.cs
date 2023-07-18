using Valour.Api.Client;
using Valour.Api.Nodes;
using Valour.Shared.Models;
using Valour.Shared.Models.Economy;

namespace Valour.Api.Models.Economy;

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
public class EcoAccount : LiveModel, ISharedEcoAccount
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
            var cached = ValourCache.Get<EcoAccount>(id);
            if (cached != null)
                return cached;
        }

        var node = await NodeManager.GetNodeForPlanetAsync(planetId);
        var item = (await node.GetJsonAsync<EcoAccount>($"api/eco/accounts/{id}")).Data;

        if (item is not null)
            await item.AddToCache();

        return item;
    }

    /// <summary>
    /// Returns all planet accounts for the given planet id
    /// </summary>
    public static async Task<List<EcoAccount>> GetPlanetPlanetAccountsAsync(long planetId)
    {
        var node = await NodeManager.GetNodeForPlanetAsync(planetId);
        var accounts = (await node.GetJsonAsync<List<EcoAccount>>($"api/eco/accounts/planet/{planetId}/planet")).Data;

        if (accounts is not null)
        {
            foreach (var account in accounts)
            {
                await account.AddToCache();
            }
        }

        return accounts;
    }
    
    /// <summary>
    /// Returns all user accounts for the given planet id
    /// </summary>
    public static async Task<List<EcoAccount>> GetPlanetUserAccountsAsync(long planetId)
    {
        var node = await NodeManager.GetNodeForPlanetAsync(planetId);
        var accounts = (await node.GetJsonAsync<List<EcoAccount>>($"api/eco/accounts/planet/{planetId}/user")).Data;

        if (accounts is not null)
        {
            foreach (var account in accounts)
            {
                await account.AddToCache();
            }
        }

        return accounts;
    }

    /// <summary>
    /// Returns all accounts the user can send to for a given planet id
    /// </summary>
    public static async Task<List<EcoAccount>> GetPlanetAccountsCanSendAsync(long planetId)
    {
        var node = await NodeManager.GetNodeForPlanetAsync(planetId);
        var accounts = (await node.GetJsonAsync<List<EcoAccount>>($"api/eco/accounts/planet/{planetId}/canSend")).Data;

        if (accounts is not null)
        {
            foreach (var account in accounts)
            {
                await account.AddToCache();
            }
        }

        return accounts;
    }

    public static async Task<EcoAccount> GetSelfGlobalAccountAsync()
    {
        var node = await NodeManager.GetNodeForPlanetAsync(ISharedPlanet.ValourCentralId);
        var account = (await node.GetJsonAsync<EcoAccount>($"api/eco/accounts/self/global")).Data;
        
        if (account is not null)
            await account.AddToCache();

        return account;
    }
}
