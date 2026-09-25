using Microsoft.EntityFrameworkCore;

namespace Valour.Database;

/// <summary>
/// Message text a reporter chose to reveal with a report, and whether the
/// server could prove the author sent it. Reports on encrypted messages are
/// the only way message text reaches moderators or staff outside the channel.
/// </summary>
public class ReportEvidence
{
    public long Id { get; set; }

    /// <summary>Platform report ID, when the evidence belongs to a report to Valour staff.</summary>
    public string ReportId { get; set; }

    /// <summary>Planet report ID, when the evidence belongs to a report to planet moderators.</summary>
    public long? PlanetReportId { get; set; }

    public long MessageId { get; set; }
    public int Revision { get; set; }
    public long ChannelId { get; set; }
    public long AuthorUserId { get; set; }
    public DateTime TimeSent { get; set; }
    public string Content { get; set; }
    public string Embed { get; set; }

    /// <summary>How the text was checked. See <c>ReportEvidenceVerification</c>.</summary>
    public int Verification { get; set; }

    public DateTime CreatedAt { get; set; }

    public static void SetupDbModel(ModelBuilder builder)
    {
        builder.Entity<ReportEvidence>(e =>
        {
            e.ToTable("report_evidence");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id").UseIdentityAlwaysColumn();
            e.Property(x => x.ReportId).HasColumnName("report_id");
            e.Property(x => x.PlanetReportId).HasColumnName("planet_report_id");
            e.Property(x => x.MessageId).HasColumnName("message_id");
            e.Property(x => x.Revision).HasColumnName("revision");
            e.Property(x => x.ChannelId).HasColumnName("channel_id");
            e.Property(x => x.AuthorUserId).HasColumnName("author_user_id");
            e.Property(x => x.TimeSent).HasColumnName("time_sent")
                .HasConversion(x => x, x => new DateTime(x.Ticks, DateTimeKind.Utc));
            e.Property(x => x.Content).HasColumnName("content");
            e.Property(x => x.Embed).HasColumnName("embed");
            e.Property(x => x.Verification).HasColumnName("verification");
            e.Property(x => x.CreatedAt).HasColumnName("created_at")
                .HasConversion(x => x, x => new DateTime(x.Ticks, DateTimeKind.Utc));
            e.HasIndex(x => x.ReportId);
            e.HasIndex(x => x.PlanetReportId);
        });
    }
}
