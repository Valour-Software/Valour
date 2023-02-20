namespace Valour.Shared.Models.Economy;

/// <summary>
/// A transaction represents a *completed* transaction between two accounts.
/// </summary>
public interface ISharedTransaction
{
    /// <summary>
    /// Unlike most ids in Valour, transactions do not use a snowflake.
    /// We anticipate some rapid transactions via market botting, which
    /// could potentially hit our snowflake id-per-second limit.
    /// Instead we use a Guid
    /// </summary>
    string Id { get; set; }

    /// <summary>
    /// The planet the transaction belongs to
    /// </summary>
    long PlanetId { get; set; }

    /// <summary>
    /// The id of the owner of the sending account
    /// </summary>
    long UserFromId { get; set; }

    /// <summary>
    /// The id of the sending account
    /// </summary>
    long AccountFromId { get; set; }

    /// <summary>
    /// The id of the owner of the receiving account
    /// </summary>
    long UserToId { get; set; }

    /// <summary>
    /// The id of the receiving account
    /// </summary>
    long AccountToId { get; set; }

    /// <summary>
    /// The time this transaction was completed
    /// </summary>
    DateTime TimeStamp { get; set; }

    /// <summary>
    /// A description of the transaction
    /// </summary>
    string Description { get; set; }
    
    /// <summary>
    /// Additional data that can be attached to a transaction
    /// </summary>
    string Data { get; set; }

    /// <summary>
    /// A value that can be used to identify a transaction completing.
    /// It should match the request fingerprint.
    /// </summary>
    string Fingerprint { get; set; }
}
