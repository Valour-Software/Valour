using Valour.Shared.Models;

namespace Valour.Server.Models;

public class Reaction : ISharedReaction
{
    /// <summary>
    /// The id of this reaction
    /// </summary>
    public long Id { get; set; }
    
    /// <summary>
    /// The id of the message this reaction is on
    /// </summary>
    public long MessageId { get; set; }
    
    /// <summary>
    /// The user who added the reaction
    /// </summary>
    public long UserId { get; set; }
}