using Microsoft.EntityFrameworkCore;

namespace Valour.Database;

public enum MfaRemovalStatus
{
    Pending = 0,
    Executed = 1,
    Cancelled = 2
}

/// <summary>
/// A staff-initiated MFA removal waiting out its safety delay. The account
/// is emailed when the removal is scheduled; a worker executes it after
/// ExecuteAt unless it is cancelled first.
/// </summary>
public class PendingMfaRemoval
{
    public virtual User TargetUser { get; set; }

    public long Id { get; set; }
    public long TargetUserId { get; set; }
    public long StaffUserId { get; set; }
    public string Reason { get; set; }
    public MfaRemovalStatus Status { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime ExecuteAt { get; set; }
    public DateTime? ResolvedAt { get; set; }

    public static void SetupDbModel(ModelBuilder builder)
    {
        builder.Entity<PendingMfaRemoval>(e =>
        {
            e.ToTable("pending_mfa_removals");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id");
            e.Property(x => x.TargetUserId).HasColumnName("target_user_id");
            e.Property(x => x.StaffUserId).HasColumnName("staff_user_id");
            e.Property(x => x.Reason).HasColumnName("reason");
            e.Property(x => x.Status).HasColumnName("status");
            e.Property(x => x.CreatedAt).HasColumnName("created_at")
                .HasConversion(x => x, x => new DateTime(x.Ticks, DateTimeKind.Utc));
            e.Property(x => x.ExecuteAt).HasColumnName("execute_at")
                .HasConversion(x => x, x => new DateTime(x.Ticks, DateTimeKind.Utc));
            e.Property(x => x.ResolvedAt).HasColumnName("resolved_at")
                .HasConversion(
                    x => x,
                    x => x.HasValue ? new DateTime(x.Value.Ticks, DateTimeKind.Utc) : null);

            e.HasOne(x => x.TargetUser)
                .WithMany()
                .HasForeignKey(x => x.TargetUserId);

            e.HasIndex(x => x.TargetUserId);
            e.HasIndex(x => new { x.Status, x.ExecuteAt });
        });
    }
}
