using System.ComponentModel.DataAnnotations.Schema;
using Valour.Shared.Models;

namespace Valour.Database;

[Table("planet_messages")]
public class PlanetMessage : Item, ISharedPlanetMessage
{
    ///////////////////////////
    // Relational Properties //
    ///////////////////////////
    
    [ForeignKey("PlanetId")]
    public Planet Planet { get; set; }

    [ForeignKey("AuthorUserId")]
    public User AuthorUser { get; set; }

    [ForeignKey("AuthorMemberId")]
    public PlanetMember AuthorMember { get; set; }

    [ForeignKey("ReplyToId")]
    public PlanetMessage ReplyToMessage { get; set; }
    
    ///////////////////////
    // Entity Properties //
    ///////////////////////
    
    [Column("planet_id")]
    public long PlanetId { get; set; }

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
    /// The author's member ID
    /// </summary>
    [Column("author_member_id")]
    public long AuthorMemberId { get; set; }

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

