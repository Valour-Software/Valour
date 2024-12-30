using System;
using Microsoft.EntityFrameworkCore;
using Valour.Shared.Models;

namespace Valour.Database
{
    public class UserSubscription : ISharedUserSubscription
    {
        public virtual User User { get; set; }
        
        /// <summary>
        /// The id of the subscription
        /// </summary>
        public string Id { get; set; }
        
        /// <summary>
        /// The id of the user who owns this subscription
        /// </summary>
        public long UserId { get; set; }
        
        /// <summary>
        /// The type of subscription this represents
        /// </summary>
        public string Type { get; set; }
        
        /// <summary>
        /// The date at which the subscription was created
        /// </summary>
        public DateTime Created { get; set; }
        
        /// <summary>
        /// The last time at which the user was charged for their subscription
        /// </summary>
        public DateTime LastCharged { get; set; }
        
        /// <summary>
        /// If the subscription is currently active.
        /// Subscriptions are not re-activated. A new subscription is created, allowing
        /// subscription lengths to be tracked.
        /// </summary>
        public bool Active { get; set; }
        
        /// <summary>
        /// If a subscription is set to cancelled, it will not be rebilled
        /// </summary>
        public bool Cancelled { get; set; }
        
        /// <summary>
        /// How many times this subscription has been renewed
        /// </summary>
        public int Renewals { get; set; }

        /// <summary>
        /// Configures the entity model for the `UserSubscription` class using fluent configuration.
        /// </summary>
        public static void SetupDbModel(ModelBuilder builder)
        {
            builder.Entity<UserSubscription>(e =>
            {
                // Table
                e.ToTable("user_subscriptions");
                
                // Keys
                e.HasKey(x => x.Id);

                // Properties
                e.Property(x => x.Id)
                    .HasColumnName("id");
                
                e.Property(x => x.UserId)
                    .HasColumnName("user_id");
                
                e.Property(x => x.Type)
                    .HasColumnName("type");
                
                e.Property(x => x.Created)
                    .HasColumnName("created")
                    .HasConversion(
                        x => x,
                        x => new DateTime(x.Ticks, DateTimeKind.Utc)
                    );
                
                e.Property(x => x.LastCharged)
                    .HasColumnName("last_charged")
                    .HasConversion(
                        x => x,
                        x => new DateTime(x.Ticks, DateTimeKind.Utc)
                    );
                
                e.Property(x => x.Active)
                    .HasColumnName("active");
                
                e.Property(x => x.Cancelled)
                    .HasColumnName("cancelled");
                
                e.Property(x => x.Renewals)
                    .HasColumnName("renewals");

                // Relationships
                e.HasOne(x => x.User)
                    .WithMany(x => x.Subscriptions)
                    .HasForeignKey(x => x.UserId);

                // Indices
            });
        }
    }
}
