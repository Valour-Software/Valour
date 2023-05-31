using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Valour.Shared.Models.Economy;

namespace Valour.Database.Economy;

[Table("eco_accounts")]
public class EcoAccount : ISharedEcoAccount
{
    /// <summary>
    /// The database id of this economy account
    /// </summary>
    [Column("id")]
    public long Id { get; set; }
    
    /// <summary>
    /// The name of the account
    /// </summary>
    [Column("name")]
    public string Name { get; set; }

    /// <summary>
    /// The type of account this represents
    /// </summary>
    [Column("account_type")]
    public AccountType AccountType { get; set; }

    /// <summary>
    /// The id of the user who opened this account
    /// This will always be set, even for planet accounts
    /// </summary>
    [Column("user_id")]
    public long UserId { get; set; }

    /// <summary>
    /// The id of the planet this economy account belongs to
    /// This will always be set
    /// </summary>
    [Column("planet_id")]
    public long PlanetId { get; set; }

    /// <summary>
    /// The id of the currency this account is using
    /// </summary>
    [Column("currency_id")]
    public long CurrencyId { get; set; }

    /// <summary>
    /// The value of the balance of this account
    /// </summary>
    [Column("balance_value")]
    public decimal BalanceValue { get; set; }
    
    /// <summary>
    /// The RowVersion column prevents concurrency issues when updating the database
    /// </summary>
    [Timestamp]
    public uint RowVersion { get; set; }
}