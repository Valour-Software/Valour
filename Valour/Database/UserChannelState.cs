using Microsoft.EntityFrameworkCore;
using Valour.Shared.Channels;

namespace Valour.Database;

public class UserChannelState : ISharedUserChannelState
{
    public virtual User User { get; set; }
    public virtual Channel Channel { get; set; }
    public virtual Planet Planet { get; set; }
    
    ///////////////////////
    // Entity Properties //
    ///////////////////////
    
    public long ChannelId { get; set; }
    public long UserId { get; set; }
    public long? PlanetId { get; set; } 
    public DateTime LastViewedTime { get; set; }
    
    public static void SetupDbModel(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<UserChannelState>(e =>
        {
            e.ToTable("user_channel_states");
            
            e.HasKey(x => new { x.UserId, x.ChannelId });
            
            e.Property(x => x.ChannelId).HasColumnName("channel_id");
            e.Property(x => x.UserId).HasColumnName("user_id");
            e.Property(x => x.PlanetId).HasColumnName("planet_id");
            e.Property(x => x.LastViewedTime)
                .HasColumnName("last_viewed_time")
                .HasConversion(
                    x => x,
                    x => new DateTime(x.Ticks, DateTimeKind.Utc)
                );

            e.HasOne(x => x.User)
                .WithMany(x => x.ChannelStates)
                .HasForeignKey(x => x.UserId);
            
            e.HasOne(x => x.Channel)
                .WithMany(x => x.UserChannelStates)
                .HasForeignKey(x => x.ChannelId);
            
            e.HasOne(x => x.Planet)
                .WithMany(x => x.UserChannelStates)
                .HasForeignKey(x => x.PlanetId);

            // Often queried by all
            e.HasIndex(x => x.ChannelId);
            e.HasIndex(x => x.UserId);
            e.HasIndex(x => x.PlanetId);
        });
    }
}
