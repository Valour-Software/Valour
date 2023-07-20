using Valour.Shared.Models.Economy;

namespace Valour.Server.Models.Economy;


/// <summary>
/// Currencies represent one *type* of cash, declared by a community.
/// </summary>
public class Currency : ISharedCurrency
{
    /// <summary>
    /// The database id of this currency
    /// </summary>
    public long Id { get; set; }

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
    
}
