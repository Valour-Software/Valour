namespace Valour.Shared.Models.Economy;

public interface ISharedCurrency
{
    /// <summary>
    /// Constant id for Valour Credits
    /// I would choose 0, but that's the default. So I'll choose 3, the best number.
    /// </summary>
    const long ValourCreditsId = 3;

    /// <summary>
    /// The database id of this currency
    /// </summary>
    long Id { get; set; }

    /// <summary>
    /// The planet this currency belongs to
    /// </summary>
    long PlanetId { get; set; }

    /// <summary>
    /// The name of this currency (ie dollar)
    /// </summary>
    string Name { get; set; }

    /// <summary>
    /// The plural name of this currency (ie dollars)
    /// </summary>
    string PluralName { get; set; }

    /// <summary>
    /// The short-code for this currency (ie USD)
    /// </summary>
    string ShortCode { get; set; }

    /// <summary>
    /// The symbol to display before the value (ie $)
    /// </summary>
    string Symbol { get; set; }

    /// <summary>
    /// The total amount of this currency that has been issued
    /// </summary>
    long Issued { get; set; }

    /// <summary>
    /// The number of decimal places this currency supports
    /// </summary>
    int DecimalPlaces { get; set; }
}
