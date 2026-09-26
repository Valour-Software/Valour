#nullable enable

using Microsoft.EntityFrameworkCore;
using Valour.Shared.Models;

namespace Valour.Database;

public class PushNotificationSubscription : ISharedPushNotificationSubscription
{
    ///////////////////////////
    // Relational Properties //
    ///////////////////////////
    
    public virtual User? User { get; set; }

    public virtual AuthToken? AuthToken { get; set; }
    
    ///////////////////////
    // Entity Properties //
    ///////////////////////
    
    public long Id { get; set; }
    
    /// <summary>
    /// The type of device this subscription is for
    /// </summary>
    public NotificationDeviceType DeviceType { get; set; }
    
    /// <summary>
    /// When this subscription expires
    /// </summary>
    public DateTime ExpiresAt { get; set; }

    /// <summary>
    /// The Id of the user this subscription is for
    /// </summary>
    public long UserId { get; set; }
    
    /// <summary>
    /// The endpoint of the subscription
    /// </summary>
    public required string Endpoint { get; set; }
    
    public string? Key { get; set; }
    
    public string? Auth { get; set; }

    /// <summary>
    /// The session that registered this subscription. Ending the session
    /// deletes the subscription, so a device whose session was revoked stops
    /// receiving notifications, which it could otherwise decrypt with the
    /// message keys it kept. Null for subscriptions registered before this
    /// was recorded.
    /// </summary>
    public string? AuthTokenId { get; set; }

    public static void SetUpDbModel(ModelBuilder builder)
    {
        builder.Entity<PushNotificationSubscription>(e =>
        {
            // ToTable
            e.ToTable("notification_subscriptions");
            
            // Key
            e.HasKey(x => x.Id);
            
            // Properties
            e.Property(x => x.Id)
                .HasColumnName("id");
            
            e.Property(x => x.DeviceType)
                .HasColumnName("device_type");
            
            e.Property(x => x.ExpiresAt)
                .HasColumnName("expires_at")
                .HasDefaultValueSql("(NOW() + INTERVAL '7 days')")
                .HasConversion(
                    x => x,
                    x => new DateTime(x.Ticks, DateTimeKind.Utc)
                );
            
            e.Property(x => x.UserId)
                .HasColumnName("user_id");
            
            e.Property(x => x.Key)
                .HasColumnName("key");
            
            e.Property(x => x.Auth)
                .HasColumnName("auth");
            
            e.Property(x => x.Endpoint)
                .HasColumnName("endpoint");

            e.Property(x => x.AuthTokenId)
                .HasColumnName("auth_token_id");
            
            // Relationships

            e.HasOne(x => x.User)
                .WithMany(x => x.NotificationSubscriptions)
                .HasForeignKey(x => x.UserId);

            e.HasOne(x => x.AuthToken)
                .WithMany()
                .HasForeignKey(x => x.AuthTokenId)
                .OnDelete(DeleteBehavior.Cascade);
            
            // Indices
            e.HasIndex(x => x.UserId);
            e.HasIndex(x => x.AuthTokenId);
        });
    }
}
