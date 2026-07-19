using Microsoft.EntityFrameworkCore;

namespace Valour.Database;

/// <summary>
/// Destination-side durable receipt for a forward migration import. It is
/// written before the snapshot is imported so a timeout or process crash can
/// resume the source handoff without importing a second copy.
/// </summary>
public class FederatedImportReceipt
{
    public long PlanetId { get; set; }
    public string SourceDomain { get; set; }
    public long OwnerId { get; set; }
    /// <summary>
    /// The hub-signed migration capability id that produced this import. It
    /// prevents a later, restarted migration from confirming a stale local
    /// snapshot from an earlier aborted attempt.
    /// </summary>
    public string GrantId { get; set; }
    public string SnapshotHash { get; set; }
    public bool SourcePublic { get; set; }
    public bool SourceDiscoverable { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? ConfirmedAt { get; set; }

    public static void SetupDbModel(ModelBuilder builder)
    {
        builder.Entity<FederatedImportReceipt>(e =>
        {
            e.ToTable("federated_import_receipts");
            e.HasKey(x => x.PlanetId);
            e.Property(x => x.PlanetId).HasColumnName("planet_id").ValueGeneratedNever();
            e.Property(x => x.SourceDomain).HasColumnName("source_domain").IsRequired();
            e.Property(x => x.OwnerId).HasColumnName("owner_id");
            e.Property(x => x.GrantId).HasColumnName("grant_id");
            e.Property(x => x.SnapshotHash).HasColumnName("snapshot_hash").IsRequired();
            e.Property(x => x.SourcePublic).HasColumnName("source_public");
            e.Property(x => x.SourceDiscoverable).HasColumnName("source_discoverable");
            e.Property(x => x.CreatedAt).HasColumnName("created_at").IsRequired();
            e.Property(x => x.ConfirmedAt).HasColumnName("confirmed_at");
            e.HasIndex(x => x.SourceDomain);
        });
    }
}
