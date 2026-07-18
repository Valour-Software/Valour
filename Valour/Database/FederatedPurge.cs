using Microsoft.EntityFrameworkCore;

namespace Valour.Database;

/// <summary>
/// A hub-side tombstone recording that a user's account was hard-deleted.
/// Community nodes pull these and purge that user's federated (shadow) data,
/// honoring account deletion across the network. Best-effort by design — the
/// join warning tells users the hub can't guarantee node compliance.
/// </summary>
public class FederatedPurge
{
    public long Id { get; set; }

    /// <summary>The deleted user's id.</summary>
    public long SubjectUserId { get; set; }

    public DateTime CreatedAt { get; set; }

    public static void SetupDbModel(ModelBuilder builder)
    {
        builder.Entity<FederatedPurge>(e =>
        {
            e.ToTable("federated_purges");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id").ValueGeneratedNever();
            e.Property(x => x.SubjectUserId).HasColumnName("subject_user_id").IsRequired();
            e.Property(x => x.CreatedAt).HasColumnName("created_at").IsRequired();
            e.HasIndex(x => x.CreatedAt);
        });
    }
}
