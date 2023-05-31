using Valour.Shared.Models.Economy;

namespace Valour.Server.Models.Economy;

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
public class EcoAccount : ISharedEcoAccount
{
    /// <summary>
    /// The database id of this economy account
    /// </summary>
    public long Id { get; set; }
    
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
}
