using System.ComponentModel.DataAnnotations.Schema;
using Valour.Shared.Items.Messages;

namespace Valour.Database;

[Table("direct_messages")]
public class DirectMessage  : Item, ISharedMessage
{
    [ForeignKey("AuthorUserId")]
    public User AuthorUser { get; set; }

    [ForeignKey("ReplyToId")]
    public DirectMessage ReplyToMessage { get; set; }

    /// <summary>
    /// The message (if any) this is a reply to
    /// </summary>
    [Column("reply_to_id")]
    public long? ReplyToId { get; set; }

    /// <summary>
    /// The author's user ID
    /// </summary>
    [Column("author_user_id")]
    public long AuthorUserId { get; set; }

    /// <summary>
    /// String representation of message
    /// </summary>
    [Column("content")]
    public string Content { get; set; }

    /// <summary>
    /// The time the message was sent (in UTC)
    /// </summary>
    [Column("time_sent")]
    public DateTime TimeSent { get; set; }

    /// <summary>
    /// Id of the channel this message belonged to
    /// </summary>
    [Column("channel_id")]
    public long ChannelId { get; set; }

    /// <summary>
    /// Data for representing an embed
    /// </summary>
    [Column("embed_data")]
    public string EmbedData { get; set; }

    /// <summary>
    /// Data for representing mentions in a message
    /// </summary>
    [Column("mentions_data")]
    public string MentionsData { get; set; }

    /// <summary>
    /// Data for representing attachments in a message
    /// </summary>
    [Column("attachments_data")]
    public string AttachmentsData { get; set; }

    /// <summary>
    /// True if the message was edited
    /// </summary>
    [Column("edited")]
    public bool Edited { get; set; }
}
