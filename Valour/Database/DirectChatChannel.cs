using System.ComponentModel.DataAnnotations.Schema;
using Valour.Shared.Models;

namespace Valour.Database;

[Table("direct_chat_channels")]
public class DirectChatChannel : Channel, ISharedDirectChatChannel
{
    ///////////////////////////
    // Relational Properties //
    ///////////////////////////
    
    /// <summary>
    /// One of the users in the DM channel
    /// </summary>
    [ForeignKey("UserOneId")]
    public virtual User UserOne { get; set; }

    /// <summary>
    /// One of the users in the DM channel
    /// </summary>
    [ForeignKey("UserTwoId")]
    public virtual User UserTwo { get; set; }
    
    ///////////////////////
    // Entity Properties //
    ///////////////////////

    /// <summary>
    /// The id of one of the users in the DM channel
    /// </summary>
    [Column("user_one_id")]
    public long UserOneId { get; set; }

    /// <summary>
    /// The id of one of the users in the DM channel
    /// </summary>
    [Column("user_two_id")]
    public long UserTwoId { get; set; }

    /// <summary>
    /// The number of messages in the channel
    /// </summary>
    [Column("message_count")]
    public long MessageCount { get; set; }
}
