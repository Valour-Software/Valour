using Microsoft.EntityFrameworkCore;

namespace Valour.Database;

public class RealtimeKitMeeting
{
    public long Id { get; set; }
    public long ChannelId { get; set; }
    public long? PlanetId { get; set; }
    public string MeetingId { get; set; }
    public string Status { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime LastUsedAt { get; set; }
    public DateTime? ClosedAt { get; set; }
    public DateTime? LastCleanupAttemptAt { get; set; }
    public int CleanupFailureCount { get; set; }
    public string LastCleanupError { get; set; }

    public static void SetupDbModel(ModelBuilder builder)
    {
        builder.Entity<RealtimeKitMeeting>(e =>
        {
            e.ToTable("realtimekit_meetings");

            e.HasKey(x => x.Id);

            e.Property(x => x.Id)
                .HasColumnName("id");

            e.Property(x => x.ChannelId)
                .HasColumnName("channel_id")
                .IsRequired();

            e.Property(x => x.PlanetId)
                .HasColumnName("planet_id");

            e.Property(x => x.MeetingId)
                .HasColumnName("meeting_id")
                .IsRequired();

            e.Property(x => x.Status)
                .HasColumnName("status")
                .IsRequired();

            e.Property(x => x.CreatedAt)
                .HasColumnName("created_at")
                .HasConversion(x => x.ToUniversalTime(), x => DateTime.SpecifyKind(x, DateTimeKind.Utc))
                .IsRequired();

            e.Property(x => x.LastUsedAt)
                .HasColumnName("last_used_at")
                .HasConversion(x => x.ToUniversalTime(), x => DateTime.SpecifyKind(x, DateTimeKind.Utc))
                .IsRequired();

            e.Property(x => x.ClosedAt)
                .HasColumnName("closed_at")
                .HasConversion(x => x.HasValue ? x.Value.ToUniversalTime() : x, x => x.HasValue ? DateTime.SpecifyKind(x.Value, DateTimeKind.Utc) : x);

            e.Property(x => x.LastCleanupAttemptAt)
                .HasColumnName("last_cleanup_attempt_at")
                .HasConversion(x => x.HasValue ? x.Value.ToUniversalTime() : x, x => x.HasValue ? DateTime.SpecifyKind(x.Value, DateTimeKind.Utc) : x);

            e.Property(x => x.CleanupFailureCount)
                .HasColumnName("cleanup_failure_count")
                .IsRequired();

            e.Property(x => x.LastCleanupError)
                .HasColumnName("last_cleanup_error");

            e.HasIndex(x => x.ChannelId)
                .IsUnique()
                .HasFilter("closed_at IS NULL");

            e.HasIndex(x => x.MeetingId)
                .IsUnique();

            e.HasIndex(x => new { x.Status, x.ClosedAt });
        });
    }
}
