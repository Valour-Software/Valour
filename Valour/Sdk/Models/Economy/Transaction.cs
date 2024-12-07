using Valour.Shared.Models.Economy;

namespace Valour.Sdk.Models.Economy;

/// <summary>
/// A transaction represents a *completed* transaction between two accounts.
/// Transactions are not a true model because they do not change.
/// </summary>
public class Transaction : ISharedTransaction
{
    /// <summary>
    /// Unlike most ids in Valour, transactions do not use a snowflake.
    /// We anticipate some rapid transactions via market botting, which
    /// could potentially hit our snowflake id-per-second limit.
    /// Instead we use a Guid
    /// </summary>
    public string Id { get; set; }

    /// <summary>
    /// The planet the transaction belongs to
    /// </summary>
    public long PlanetId { get; set; }

    /// <summary>
    /// The id of the owner of the sending account
    /// </summary>
    public long UserFromId { get; set; }

    /// <summary>
    /// The id of the sending account
    /// </summary>
    public long AccountFromId { get; set; }

    /// <summary>
    /// The id of the owner of the receiving account
    /// </summary>
    public long UserToId { get; set; }

    /// <summary>
    /// The id of the receiving account
    /// </summary>
    public long AccountToId { get; set; }

    /// <summary>
    /// The time this transaction was completed
    /// </summary>
    public DateTime TimeStamp { get; set; }

    /// <summary>
    /// A description of the transaction
    /// </summary>
    public string Description { get; set; }

    /// <summary>
    /// The amount of currency transferred in the transaction
    /// </summary>
    public decimal Amount { get; set; }

    /// <summary>
    /// Additional data that can be attached to a transaction
    /// </summary>
    public string Data { get; set; }

    /// <summary>
    /// A value that can be used to identify a transaction completing.
    /// It should match the request fingerprint.
    /// </summary>
    public string Fingerprint { get; set; }
    
    /// <summary>
    /// If this transaction was forced by an Eco Admin, this is the id of the user who forced it.
    /// </summary>
    public long? ForcedBy { get; set; }
}