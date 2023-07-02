using System.ComponentModel.DataAnnotations.Schema;
using Valour.Shared.Models;

namespace Valour.Database;

[Table("notifications")]
public class Notification : ISharedNotification
{
    /// <summary>
    /// The id of the notification
    /// </summary>
    [Column("id")]
    public long Id { get; set; }
    
    /// <summary>
    /// The user the notification was sent to
    /// </summary>
    [Column("user_id")]
    public long UserId { get; set; }
    
    /// <summary>
    /// The planet (if any) the notification came from
    /// </summary>
    [Column("planet_id")]
    public long? PlanetId { get; set; }
    
    /// <summary>
    /// The channel (if any) the notification came from
    /// </summary>
    [Column("channel_id")]
    public long? ChannelId { get; set; }
    
    /// <summary>
    /// The source (if any) the notification came from
    /// This can represent different things, depending on the NotificationSource
    /// For example, if the source is a message, this will be the message id
    /// If it's a friend request, this will be the friend user's id
    /// </summary>
    [Column("source_id")]
    public long? SourceId { get; set; }
    
    /// <summary>
    /// The source of the notification
    /// </summary>
    [Column("source")]
    public NotificationSource Source { get; set; }
}