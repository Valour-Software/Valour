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
    public virtual PlanetMember? Member { get; set; }
    public virtual Planet? Planet { get; set; }
    
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
    /// The Id of the planet (if any) this subscription is for
    /// </summary>
    public long? PlanetId { get; set; }
    
    /// <summary>
    /// The Id of the member this subscription is for, if a planet subscription.
    /// </summary>
    public long? MemberId { get; set; }
    
    /// <summary>
    /// The RoleHashKey the planet member is subscribed to. Should only be used for planet subscriptions.
    /// </summary>
    public long? RoleHashKey { get; set; }
    
    public required string Endpoint { get; set; }
    
    public string? Key { get; set; }
    
    public string? Auth { get; set; }

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
            
            e.Property(x => x.PlanetId)
                .HasColumnName("planet_id");
            
            e.Property(x => x.MemberId)
                .HasColumnName("member_id");
            
            e.Property(x => x.RoleHashKey)
                .HasColumnName("role_hash_key");
            
            e.Property(x => x.Key)
                .HasColumnName("key");
            
            e.Property(x => x.Auth)
                .HasColumnName("auth");
            
            e.Property(x => x.Endpoint)
                .HasColumnName("endpoint");
            
            // Relationships

            e.HasOne(x => x.User)
                .WithMany(x => x.NotificationSubscriptions)
                .HasForeignKey(x => x.UserId);
            
            e.HasOne(x => x.Planet)
                .WithMany(x => x.NotificationSubscriptions)
                .HasForeignKey(x => x.PlanetId);
            
            e.HasOne(x => x.Member)
                .WithMany(x => x.PushSubscriptions)
                .HasForeignKey(x => x.MemberId);
            
            // Indices

            e.HasIndex(x => x.UserId);
            e.HasIndex(x => x.PlanetId);
            
            e.HasIndex(x => x.MemberId)
                .IsUnique();
        });
    }
}
