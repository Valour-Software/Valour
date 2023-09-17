namespace Valour.Shared.Models.Economy;

public interface ISharedEcoAccount
{
    /// <summary>
    /// The database id of this economy account
    /// </summary>
    long Id { get; set; }
    
    /// <summary>
    /// The name of the account
    /// </summary>
    string Name { get; set; }

    /// <summary>
    /// The type of account this represents
    /// </summary>
    AccountType AccountType { get; set; }

    /// <summary>
    /// The id of the user who opened this account
    /// This will always be set, even for planet accounts
    /// </summary>
    long UserId { get; set; }

    /// <summary>
    /// The id of the planet this economy account belongs to
    /// This will always be set
    /// </summary>
    long PlanetId { get; set; }
    
    /// <summary>
    /// The member id of the planet member this account belongs to
    /// </summary>
    long? PlanetMemberId { get; set; }

    /// <summary>
    /// The id of the currency this account is using
    /// </summary>
    long CurrencyId { get; set; }

    /// <summary>
    /// The value of the balance of this account
    /// This should *not* be used in code. Use Balance instead.
    /// This is just for mapping to the database.
    /// </summary>
    decimal BalanceValue { get; set; }
}
