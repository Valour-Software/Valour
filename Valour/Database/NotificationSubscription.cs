using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;
using Valour.Shared.Models;

namespace Valour.Database;

[Table("notification_subscriptions")]
public class NotificationSubscription : ISharedNotificationSubscription
{
    ///////////////////////////
    // Relational Properties //
    ///////////////////////////
    
    [ForeignKey("UserId")]
    public User User { get; set; }
    
    ///////////////////////
    // Entity Properties //
    ///////////////////////

    [Key]
    [Column("id")]
    public long Id {get; set; }

    /// <summary>
    /// The Id of the user this subscription is for
    /// </summary>
    [Column("user_id")]
    public long UserId { get; set; }

    [Column("endpoint")]
    public string Endpoint { get; set; }

    [Column("key")]
    public string Key { get; set; }

    [Column("auth")]
    public string Auth { get; set; }

    public static void SetUpDbModel(ModelBuilder builder)
    {
        builder.Entity<NotificationSubscription>(e =>
        {
            // ToTable
            e.ToTable("notification_subscriptions");
            
            // Key
            e.HasKey(x => x.Id);
            
            // Properties
            e.Property(x => x.Id)
                .HasColumnName("id");
            
            e.Property(x => x.Endpoint)
                .HasColumnName("endpoint");
            
            e.Property(x => x.UserId)
                .HasColumnName("user_id");
            
            e.Property(x => x.Key)
                .HasColumnName("key");
            
            e.Property(x => x.Auth)
                .HasColumnName("auth");
            
            // Relationships

            e.HasOne(x => x.User)
                .WithMany(x => x.NotificationSubscriptions)
                .HasForeignKey(x => x.UserId);
            
            // Indices
            
            e.HasIndex(x => x.UserId)
                .IsUnique();
            
            e.HasIndex(x => x.Id)
                .IsUnique();

        });
    }
}
