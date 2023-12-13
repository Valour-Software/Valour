namespace Valour.Shared.Models;

public interface ISharedReaction
{
    /// <summary>
    /// The id of this reaction
    /// </summary>
    long Id { get; set; }
    
    /// <summary>
    /// The id of the message this reaction is on
    /// </summary>
    long MessageId { get; set; }
    
    /// <summary>
    /// The user who added the reaction
    /// </summary>
    long UserId { get; set; }
}