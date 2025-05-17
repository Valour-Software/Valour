using Valour.Shared.Models.MessageReactions;

namespace Valour.Sdk.Models;

public class MessageReaction : ISharedMessageReaction
{
    /// <summary>
    /// The ID of this reaction
    /// </summary>
    public long Id { get; set; }
    
    /// <summary>
    /// The emoji used for the reaction
    /// </summary>
    public required string Emoji { get; set; }
    
    /// <summary>
    /// The message this reaction belongs to
    /// </summary>
    public long MessageId { get; set; }
    
    /// <summary>
    /// The ID of the user who reacted
    /// </summary>
    public long AuthorUserId { get; set; }
    
    /// <summary>
    /// If in a planet channel, the ID of the member this reaction belongs to
    /// </summary>
    public long? AuthorMemberId { get; set; }
    
    /// <summary>
    /// The time this reaction was created
    /// </summary>
    public DateTime CreatedAt { get; set; }
}