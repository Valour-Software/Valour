using Microsoft.EntityFrameworkCore;
using Valour.Shared.Models;

namespace Valour.Database;

/// <summary>
/// Per-user, per-planet notification settings. Kept separate from
/// PlanetMember on purpose: member rows are synced to other planet members,
/// and personal notification preferences must not be.
/// </summary>
public class UserPlanetSetting
{
    public long UserId { get; set; }
    public long PlanetId { get; set; }
    public ChannelActivityAlerts ActivityAlerts { get; set; }

    public static void SetupDbModel(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<UserPlanetSetting>(e =>
        {
            e.ToTable("user_planet_settings");

            e.HasKey(x => new { x.UserId, x.PlanetId });

            e.Property(x => x.UserId).HasColumnName("user_id");
            e.Property(x => x.PlanetId).HasColumnName("planet_id");
            e.Property(x => x.ActivityAlerts)
                .HasColumnName("activity_alerts")
                .HasDefaultValue(ChannelActivityAlerts.Auto)
                .IsRequired();

            // Evaluation queries by planet for muted users
            e.HasIndex(x => x.PlanetId);
        });
    }
}
