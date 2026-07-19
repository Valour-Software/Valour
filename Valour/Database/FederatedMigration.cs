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

    /// <summary>
    /// The current migration capability id. Re-issuing a grant rotates this
    /// value so a leaked or superseded bearer cannot be replayed; the current
    /// grant may be replayed only for the idempotent recovery steps.
    /// </summary>
    public string GrantId { get; set; }

    public DateTime? CompletedAt { get; set; }

    /// <summary>
    /// Original visibility settings captured immediately before a completed
    /// handoff hides the official recovery copy. Retained as handoff audit
    /// metadata; a completed handoff is not reopened locally because that
    /// would produce two writable copies.
    /// </summary>
    public bool? SourcePublic { get; set; }

    public bool? SourceDiscoverable { get; set; }

    /// <summary>
    /// When the source served the snapshot to the target. Completion is refused
    /// until this is set — a node can't make the source delete data it never
    /// pulled.
    /// </summary>
    public DateTime? SnapshotServedAt { get; set; }

    /// <summary>
    /// SHA-256 (hex) of the exact snapshot bytes served. Completion requires the
    /// target to echo this, proving it imported precisely what was served.
    /// </summary>
    public string SnapshotHash { get; set; }

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
            e.Property(x => x.GrantId).HasColumnName("grant_id");
            e.Property(x => x.CompletedAt).HasColumnName("completed_at");
            e.Property(x => x.SourcePublic).HasColumnName("source_public");
            e.Property(x => x.SourceDiscoverable).HasColumnName("source_discoverable");
            e.Property(x => x.SnapshotServedAt).HasColumnName("snapshot_served_at");
            e.Property(x => x.SnapshotHash).HasColumnName("snapshot_hash");
        });
    }
}
