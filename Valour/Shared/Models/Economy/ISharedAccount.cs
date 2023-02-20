namespace Valour.Shared.Models.Economy;

public interface ISharedAccount
{
    /// <summary>
    /// The database id of this economy account
    /// </summary>
    long Id { get; set; }

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
    /// The id of the currency this account is using
    /// </summary>
    long CurrencyId { get; set; }

    /// <summary>
    /// The value of the balance of this account
    /// This should *not* be used in code. Use Balance instead.
    /// This is just for mapping to the database.
    /// </summary>
    decimal BalanceValue { get; set; }

    /// <summary>
    /// Returns the balance of an account
    /// </summary>
    public static Cash GetBalance(ISharedAccount account)
        => new Cash(account.BalanceValue);

    /// <summary>
    /// Sets the balance of an account
    /// </summary>
    public static void SetBalance(ISharedAccount account, Cash balance)
        => account.BalanceValue = balance.Value;
}
