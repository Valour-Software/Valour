using Microsoft.EntityFrameworkCore;
using Valour.Shared.Models;

namespace Valour.Database;

public class Notification : ISharedNotification
{
    /// <summary>
    /// The id of the notification
    /// </summary>
    public Guid Id { get; set; }
    
    /// <summary>
    /// The user the notification was sent to
    /// </summary>
    public long UserId { get; set; }
    
    /// <summary>
    /// The planet (if any) the notification came from
    /// </summary>
    public long? PlanetId { get; set; }
    
    /// <summary>
    /// The channel (if any) the notification came from
    /// </summary>
    public long? ChannelId { get; set; }
    
    /// <summary>
    /// The source (if any) the notification came from
    /// This can represent different things, depending on the NotificationSource
    /// For example, if the source is a message, this will be the message id
    /// If it's a friend request, this will be the friend user's id
    /// </summary>
    public long? SourceId { get; set; }
    
    /// <summary>
    /// The source of the notification
    /// </summary>
    public NotificationSource Source { get; set; }
    
    /// <summary>
    /// The time at which the notification was sent
    /// </summary>
    public DateTime TimeSent { get; set; }
    
    /// <summary>
    /// The time at which the notification was read
    /// </summary>
    public DateTime? TimeRead { get; set; }
    
    /// <summary>
    /// The title of the notification
    /// </summary>
    public string Title { get; set; }
    
    /// <summary>
    /// The body of the notification
    /// </summary>
    public string Body { get; set; }
    
    /// <summary>
    /// The image url of the notification
    /// </summary>
    public string ImageUrl { get; set; }
    
    /// <summary>
    /// The url the user is brought to when the notification is clicked
    /// </summary>
    public string ClickUrl { get; set; }

    public static void SetupDbModel(ModelBuilder builder)
    {
        builder.Entity<Notification>(e =>
        {
            // ToTable
            e.ToTable("notifications");

            // Key
            e.HasKey(x => x.Id);

            // Properties
            e.Property(x => x.Id)
                .HasColumnName("id");

            e.Property(x => x.UserId)
                .HasColumnName("user_id");

            e.Property(x => x.PlanetId)
                .HasColumnName("planet_id");

            e.Property(x => x.ChannelId)
                .HasColumnName("channel_id");

            e.Property(x => x.SourceId)
                .HasColumnName("source_id");

            e.Property(x => x.Source)
                .HasColumnName("source");

            e.Property(x => x.TimeSent)
                .HasColumnName("time_sent");

            e.Property(x => x.TimeRead)
                .HasColumnName("time_read");

            e.Property(x => x.Title)
                .HasColumnName("title");

            e.Property(x => x.Body)
                .HasColumnName("body");

            e.Property(x => x.ImageUrl)
                .HasColumnName("image_url");

            e.Property(x => x.ClickUrl)
                .HasColumnName("click_url");

            // Indices

            // Hot path: unread notifications for a user, ordered by time sent.
            // Partial index keeps it tiny since only unread rows are indexed.
            e.HasIndex(x => new { x.UserId, x.TimeSent })
                .HasFilter("time_read IS NULL");

            // Supports retention cleanup of old, already-read notifications.
            e.HasIndex(x => x.TimeRead);
        });
    }
}
