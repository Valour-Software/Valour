using Valour.Database;
using Valour.Shared.Models;

namespace Valour.Server.Models;

public class Message : ServerModel<long>, ISharedMessage
{
    public Message ReplyTo { get; set; }
    public List<MessageReaction> Reactions { get; set; }
    public List<Valour.Sdk.Models.MessageAttachment> Attachments { get; set; }
    public List<Mention> Mentions { get; set; }

    public Message AddReplyTo(Message replyTo)
    {
        ReplyTo = replyTo;
        return this;
    }

    public void SetAttachments(List<Valour.Sdk.Models.MessageAttachment> attachments)
    {
        Attachments = attachments;
    }

    public void SetMentions(List<Mention> mentions)
    {
        Mentions = mentions;
    }
    
    /// <summary>
    /// The id of the planet this message belongs to
    /// </summary>
    public long? PlanetId { get; set; }

    /// <summary>
    /// The message (if any) this is a reply to
    /// </summary>
    public long? ReplyToId { get; set; }

    /// <summary>
    /// The author's user ID
    /// </summary>
    public long AuthorUserId { get; set; }

    /// <summary>
    /// The author's member ID
    /// </summary>
    public long? AuthorMemberId { get; set; }

    /// <summary>
    /// String representation of message
    /// </summary>
    public string Content { get; set; }

    /// <summary>
    /// The time the message was sent (in UTC)
    /// </summary>
    public DateTime TimeSent { get; set; }

    /// <summary>
    /// Id of the channel this message belonged to
    /// </summary>
    public long ChannelId { get; set; }
    
    /// <summary>
    /// The time when the message was edited, or null if it was not
    /// </summary>
    public DateTime? EditedTime { get; set; }

    /// <summary>
    /// Used to identify a message returned from the server 
    /// </summary>
    public string Fingerprint { get; set; }
}
