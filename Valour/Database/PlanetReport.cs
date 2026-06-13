using Microsoft.EntityFrameworkCore;
using Valour.Shared.Models;

namespace Valour.Database;

public class PlanetReport : ISharedPlanetReport
{
    ///////////////////////////
    // Relational Properties //
    ///////////////////////////

    public Planet Planet { get; set; }

    ///////////////////////
    // Entity Properties //
    ///////////////////////

    public long Id { get; set; }
    public long PlanetId { get; set; }
    public DateTime TimeCreated { get; set; }
    public long ReportingUserId { get; set; }
    public long? ReportedUserId { get; set; }
    public long? ReportedMemberId { get; set; }
    public long? MessageId { get; set; }
    public long? ChannelId { get; set; }
    public long? ThreadId { get; set; }
    public long? ThreadCommentId { get; set; }
    public long? RuleId { get; set; }
    public string RuleTitleSnapshot { get; set; }
    public string RuleDescriptionSnapshot { get; set; }
    public string LongReason { get; set; }
    public bool Reviewed { get; set; }
    public ReportResolution Resolution { get; set; }
    public long? ResolvedById { get; set; }
    public DateTime? ResolvedAt { get; set; }
    public string ModeratorNotes { get; set; }

    public static void SetupDbModel(ModelBuilder builder)
    {
        builder.Entity<PlanetReport>(e =>
        {
            e.ToTable("planet_reports");

            e.HasKey(x => x.Id);

            e.Property(x => x.Id)
                .HasColumnName("id");

            e.Property(x => x.PlanetId)
                .HasColumnName("planet_id");

            e.Property(x => x.TimeCreated)
                .HasColumnName("time_created")
                .HasConversion(
                    x => x,
                    x => new DateTime(x.Ticks, DateTimeKind.Utc));

            e.Property(x => x.ReportingUserId)
                .HasColumnName("reporting_user_id");

            e.Property(x => x.ReportedUserId)
                .HasColumnName("reported_user_id");

            e.Property(x => x.ReportedMemberId)
                .HasColumnName("reported_member_id");

            e.Property(x => x.MessageId)
                .HasColumnName("message_id");

            e.Property(x => x.ChannelId)
                .HasColumnName("channel_id");

            e.Property(x => x.ThreadId)
                .HasColumnName("thread_id");

            e.Property(x => x.ThreadCommentId)
                .HasColumnName("thread_comment_id");

            e.Property(x => x.RuleId)
                .HasColumnName("rule_id");

            e.Property(x => x.RuleTitleSnapshot)
                .HasColumnName("rule_title_snapshot")
                .HasMaxLength(ISharedPlanetReport.MaxRuleTitleSnapshotLength);

            e.Property(x => x.RuleDescriptionSnapshot)
                .HasColumnName("rule_description_snapshot")
                .HasMaxLength(ISharedPlanetReport.MaxRuleDescriptionSnapshotLength);

            e.Property(x => x.LongReason)
                .HasColumnName("long_reason")
                .HasMaxLength(ISharedPlanetReport.MaxReasonLength);

            e.Property(x => x.Reviewed)
                .HasColumnName("reviewed");

            e.Property(x => x.Resolution)
                .HasColumnName("resolution");

            e.Property(x => x.ResolvedById)
                .HasColumnName("resolved_by_id");

            e.Property(x => x.ResolvedAt)
                .HasColumnName("resolved_at")
                .HasConversion(
                    x => x,
                    x => x.HasValue ? new DateTime(x.Value.Ticks, DateTimeKind.Utc) : null);

            e.Property(x => x.ModeratorNotes)
                .HasColumnName("moderator_notes")
                .HasMaxLength(ISharedPlanetReport.MaxModeratorNotesLength);

            e.HasOne(x => x.Planet)
                .WithMany(x => x.Reports)
                .HasForeignKey(x => x.PlanetId)
                .OnDelete(DeleteBehavior.Cascade);

            e.HasIndex(x => x.PlanetId);
            e.HasIndex(x => x.TimeCreated);
            e.HasIndex(x => x.ReportingUserId);
            e.HasIndex(x => x.ReportedUserId);
            e.HasIndex(x => x.ReportedMemberId);
            e.HasIndex(x => x.RuleId);
            e.HasIndex(x => x.ThreadId);
            e.HasIndex(x => x.ThreadCommentId);
            e.HasIndex(x => x.Resolution);
            e.HasIndex(x => x.ResolvedById);
        });
    }
}
