namespace Valour.Shared.Models.Economy;

/// <summary>
/// Cash represents an amount of currency. This wrapper is used
/// to ensure consistent behavior. You should not modify this value,
/// and instead use transactions to do any cash transfers.
/// </summary>
public struct Cash
{
    /// <summary>
    /// The inner value of the cash instance
    /// </summary>
    private decimal _value = 0;

    /// <summary>
    /// The currency type of this cash
    /// </summary>
    private ISharedCurrency _currency;

    public Cash(ISharedCurrency currency)
    {
        _currency = currency;
        _value = 0;
    }

    /// <summary>
    /// Create a new instance of Cash with a value.
    /// Remember this will get rounded to 2 places! 
    /// </summary>
    public Cash(ISharedCurrency currency, decimal value)
    {
        _currency = currency;
        _value = Math.Round(value, _currency.DecimalPlaces);
    }

    /// <summary>
    /// Returns the inner decimal value of the cash
    /// </summary>
    public decimal Value => _value;

    public override string ToString() =>
        $"{_currency.Symbol}{_value.ToString("n2")}";
}