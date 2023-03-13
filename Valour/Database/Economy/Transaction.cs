using System.ComponentModel.DataAnnotations.Schema;
using Valour.Shared.Models.Economy;

namespace Valour.Database.Economy;

/// <summary>
/// A transaction represents a *completed* transaction between two accounts.
/// </summary>
[Table("transactions")]
public class Transaction : ISharedTransaction
{
    /// <summary>
    /// Unlike most ids in Valour, transactions do not use a snowflake.
    /// We anticipate some rapid transactions via market botting, which
    /// could potentially hit our snowflake id-per-second limit.
    /// Instead we use a Guid
    /// </summary>
    [Column("id")]
    public string Id { get; set; }

    /// <summary>
    /// The planet the transaction belongs to
    /// </summary>
    [Column("planet_id")]
    public long PlanetId { get; set; }


    /// <summary>
    /// The id of the owner of the sending account
    /// </summary>
    [Column("user_from_id")]
    public long UserFromId { get; set; }

    /// <summary>
    /// The id of the sending account
    /// </summary>
    [Column("account_from_id")]
    public long AccountFromId { get; set; }

    /// <summary>
    /// The id of the owner of the receiving account
    /// </summary>
    [Column("user_to_id")]
    public long UserToId { get; set; }

    /// <summary>
    /// The id of the receiving account
    /// </summary>
    [Column("account_to_id")]
    public long AccountToId { get; set; }

    /// <summary>
    /// The time this transaction was completed
    /// </summary>
    [Column("time_stamp")]
    public DateTime TimeStamp { get; set; }

    /// <summary>
    /// A description of the transaction
    /// </summary>
    [Column("description")]
    public string Description { get; set; }
    
    /// <summary>
    /// Additional data that can be attached to a transaction
    /// </summary>
    [Column("data")]
    public string Data { get; set; }

    /// <summary>
    /// A value that can be used to identify a transaction completing.
    /// It should match the request fingerprint.
    /// </summary>
    [Column("fingerprint")]
    public string Fingerprint { get; set; }
    
    /// <summary>
    /// If this transaction was forced by an Eco Admin, this is the id of the user who forced it.
    /// </summary>
    [Column("forced_by")]
    public long? ForcedBy { get; set; }
}
