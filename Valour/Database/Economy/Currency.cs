using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Valour.Shared.Models.Economy;

namespace Valour.Database.Economy;

/// <summary>
/// Currencies represent one *type* of cash, declared by a community.
/// </summary>
[Table("currencies")]
public class Currency : ISharedCurrency
{
    /// <summary>
    /// The database id of this currency
    /// </summary>
    [Column("id")]
    public long Id { get; set; }

    /// <summary>
    /// The planet this currency belongs to
    /// </summary>
    [Column("planet_id")]
    public long PlanetId { get; set; }

    /// <summary>
    /// The name of this currency (ie dollar)
    /// </summary>
    [Column("name")]
    public string Name { get; set; }

    /// <summary>
    /// The plural name of this currency (ie dollars)
    /// </summary>
    [Column("plural_name")]
    public string PluralName { get; set; }

    /// <summary>
    /// The short-code for this currency (ie USD)
    /// </summary>
    [Column("short_code")]
    public string ShortCode { get; set; }

    /// <summary>
    /// The symbol to display before the value (ie $)
    /// </summary>
    [Column("symbol")]
    public string Symbol { get; set; }

    /// <summary>
    /// The total amount of this currency that has been issued
    /// </summary>
    [Column("issued")]
    public int Issued { get; set; }

    /// <summary>
    /// The number of decimal places this currency supports
    /// </summary>
    [Column("decimal_places")]
    public int DecimalPlaces { get; set; }
    
    /// <summary>
    /// Column to protect from concurrency errors
    /// </summary>
    [Timestamp]
    public uint RowVersion { get; set; }
}