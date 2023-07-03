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
    
    /// <summary>
    /// The time at which the notification was sent
    /// </summary>
    [Column("time_sent")]
    public DateTime TimeSent { get; set; }
    
    /// <summary>
    /// The time at which the notification was read
    /// </summary>
    [Column("time_read")]
    public DateTime? TimeRead { get; set; }
    
    /// <summary>
    /// The title of the notification
    /// </summary>
    [Column("title")]
    public string Title { get; set; }
    
    /// <summary>
    /// The body of the notification
    /// </summary>
    [Column("description")]
    public string Body { get; set; }
    
    /// <summary>
    /// The image url of the notification
    /// </summary>
    [Column("image_url")]
    public string ImageUrl { get; set; }
    
    /// <summary>
    /// The url the user is brought to when the notification is clicked
    /// </summary>
    [Column("click_url")]
    public string ClickUrl { get; set; }
}