namespace Valour.Shared.Models.MessageReactions;

public interface ISharedMessageReaction
{
    /// <summary>
    /// The ID of the reaction
    /// </summary>
    long Id { get; set; }
    
    /// <summary>
    /// The emoji used for the reaction
    /// </summary>
    string Emoji { get; set; }
    
    /// <summary>
    /// The message this reaction belongs to
    /// </summary>
    long MessageId { get; set; }
    
    /// <summary>
    /// The ID of the user who reacted
    /// </summary>
    long AuthorUserId { get; set; }
    
    /// <summary>
    /// If in a planet channel, the ID of the member this reaction belongs to
    /// </summary>
    long? AuthorMemberId { get; set; }
    
    /// <summary>
    /// The time this reaction was created
    /// </summary>
    DateTime CreatedAt { get; set; }
}