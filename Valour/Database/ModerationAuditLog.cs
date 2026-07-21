using Microsoft.EntityFrameworkCore;
using Valour.Shared.Models.Staff;

namespace Valour.Database;

public class ModerationAuditLog : ISharedModerationAuditLog
{
    public long Id { get; set; }
    public long PlanetId { get; set; }
    public long? ActorUserId { get; set; }
    public long? TargetUserId { get; set; }
    public long? TargetMemberId { get; set; }
    public long? MessageId { get; set; }
    public Guid? TriggerId { get; set; }
    public ModerationActionSource Source { get; set; }
    public ModerationActionType ActionType { get; set; }
    public string Details { get; set; }
    public DateTime TimeCreated { get; set; }

    public static void SetupDbModel(ModelBuilder builder)
    {
        builder.Entity<ModerationAuditLog>(e =>
        {
            e.ToTable("moderation_audit_logs");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id");
            e.Property(x => x.PlanetId).HasColumnName("planet_id");
            e.Property(x => x.ActorUserId).HasColumnName("actor_user_id");
            e.Property(x => x.TargetUserId).HasColumnName("target_user_id");
            e.Property(x => x.TargetMemberId).HasColumnName("target_member_id");
            e.Property(x => x.MessageId).HasColumnName("message_id");
            e.Property(x => x.TriggerId).HasColumnName("trigger_id");
            e.Property(x => x.Source).HasColumnName("source");
            e.Property(x => x.ActionType).HasColumnName("action_type");
            e.Property(x => x.Details)
                .HasColumnName("details")
                .IsRequired(false);
            e.Property(x => x.TimeCreated).HasColumnName("time_created");
            e.HasIndex(x => x.PlanetId);
            e.HasIndex(x => x.TimeCreated);
            e.HasIndex(x => x.TargetUserId);
            e.HasIndex(x => x.ActorUserId);
        });
    }
}
