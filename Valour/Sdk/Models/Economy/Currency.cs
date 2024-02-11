using Valour.Sdk.Client;
using Valour.Sdk.Nodes;
using Valour.Shared.Models;
using Valour.Shared.Models.Economy;

namespace Valour.Sdk.Models.Economy;

/// <summary>
/// Currencies represent one *type* of cash, declared by a community.
/// </summary>
public class Currency : LiveModel, ISharedCurrency
{
    public override string BaseRoute => "api/eco/currencies";

    /// <summary>
    /// The planet this currency belongs to
    /// </summary>
    public long PlanetId { get; set; }

    /// <summary>
    /// The name of this currency (ie dollar)
    /// </summary>
    public string Name { get; set; }

    /// <summary>
    /// The plural name of this currency (ie dollars)
    /// </summary>
    public string PluralName { get; set; }

    /// <summary>
    /// The short-code for this currency (ie USD)
    /// </summary>
    public string ShortCode { get; set; }

    /// <summary>
    /// The symbol to display before the value (ie $)
    /// </summary>
    public string Symbol { get; set; }

    /// <summary>
    /// The total amount of this currency that has been issued
    /// </summary>
    public long Issued { get; set; }

    /// <summary>
    /// The number of decimal places this currency supports
    /// </summary>
    public int DecimalPlaces { get; set; }

    public string Format(decimal amount)
    {
        return $"{Symbol}{Math.Round(amount, DecimalPlaces)} {ShortCode}";
    }

    public static async ValueTask<Currency> FindAsync(long id, long planetId, bool refresh = false)
    {
        if (!refresh)
        {
            var cached = ValourCache.Get<Currency>(id);
            if (cached != null)
                return cached;
        }

        var node = await NodeManager.GetNodeForPlanetAsync(planetId);
        var item = (await node.GetJsonAsync<Currency>($"api/eco/currencies/{id}")).Data;

        if (item is not null)
            await item.AddToCache(item);

        return item;
    }
    
    public static async ValueTask<Currency> FindByPlanetAsync(long planetId)
    {
        var node = await NodeManager.GetNodeForPlanetAsync(planetId);
        var item = (await node.GetJsonAsync<Currency>($"api/eco/currencies/byPlanet/{planetId}", true)).Data;

        if (item is not null)
            await item.AddToCache(item);

        return item;
    }
}