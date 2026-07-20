using Microsoft.EntityFrameworkCore;
using Valour.Shared.Models.Staff;

namespace Valour.Database;

/// <summary>
/// Platform-level audit trail for staff tooling. Every sensitive staff
/// action (PII lookups, account mutations, credential resets) writes a row.
/// </summary>
public class StaffAuditLog : ISharedStaffAuditLog
{
    public long Id { get; set; }
    public long StaffUserId { get; set; }
    public StaffActionType ActionType { get; set; }
    public long? TargetUserId { get; set; }
    public string Reason { get; set; }
    public string? Details { get; set; }
    public DateTime TimeCreated { get; set; }

    public static void SetupDbModel(ModelBuilder builder)
    {
        builder.Entity<StaffAuditLog>(e =>
        {
            e.ToTable("staff_audit_logs");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id");
            e.Property(x => x.StaffUserId).HasColumnName("staff_user_id");
            e.Property(x => x.ActionType).HasColumnName("action_type");
            e.Property(x => x.TargetUserId).HasColumnName("target_user_id");
            e.Property(x => x.Reason).HasColumnName("reason");
            e.Property(x => x.Details).HasColumnName("details");
            e.Property(x => x.TimeCreated).HasColumnName("time_created")
                .HasConversion(
                    x => x,
                    x => new DateTime(x.Ticks, DateTimeKind.Utc)
                );
            e.HasIndex(x => x.TimeCreated);
            e.HasIndex(x => x.StaffUserId);
            e.HasIndex(x => x.TargetUserId);
        });
    }
}
