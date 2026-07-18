using Microsoft.EntityFrameworkCore;

namespace Valour.Database;

public enum FederatedMigrationStatus
{
    Pending = 0,
    Completed = 1,
    Aborted = 2,
}

/// <summary>
/// Tracks an in-progress planet migration on the source instance. The row is
/// the authorization record + lock: while a Pending migration exists for a
/// planet, the source treats it as locked-for-migration, and it gates the
/// snapshot pull and completion. Keyed by planet id — one migration at a time.
/// </summary>
public class FederatedMigration
{
    public long PlanetId { get; set; }

    /// <summary>
    /// Domain the planet is migrating TO (a node domain, or the hub root for
    /// a migration back to official).
    /// </summary>
    public string TargetDomain { get; set; }

    public FederatedMigrationStatus Status { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime? CompletedAt { get; set; }

    public static void SetupDbModel(ModelBuilder builder)
    {
        builder.Entity<FederatedMigration>(e =>
        {
            e.ToTable("federated_migrations");

            e.HasKey(x => x.PlanetId);

            e.Property(x => x.PlanetId).HasColumnName("planet_id").ValueGeneratedNever();
            e.Property(x => x.TargetDomain).HasColumnName("target_domain").IsRequired();
            e.Property(x => x.Status).HasColumnName("status").IsRequired();
            e.Property(x => x.CreatedAt).HasColumnName("created_at").IsRequired();
            e.Property(x => x.CompletedAt).HasColumnName("completed_at");
        });
    }
}
