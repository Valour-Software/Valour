using Microsoft.EntityFrameworkCore;

namespace Valour.Database;

/// <summary>
/// Hub-side admission rule for a community node. The composite key makes an
/// approval idempotent and prevents a node operator from accidentally creating
/// duplicate permissions. PlanetId 0 means every official planet owned by
/// OwnerId; a positive id scopes the approval to exactly one planet.
/// </summary>
public class FederatedMigrationHostingApproval
{
    public string NodeDomain { get; set; }

    public long OwnerId { get; set; }

    public long PlanetId { get; set; }

    public DateTime CreatedAt { get; set; }

    public static void SetupDbModel(ModelBuilder builder)
    {
        builder.Entity<FederatedMigrationHostingApproval>(e =>
        {
            e.ToTable("federated_migration_hosting_approvals");

            e.HasKey(x => new { x.NodeDomain, x.OwnerId, x.PlanetId });

            e.Property(x => x.NodeDomain).HasColumnName("node_domain");
            e.Property(x => x.OwnerId).HasColumnName("owner_id");
            e.Property(x => x.PlanetId).HasColumnName("planet_id");
            e.Property(x => x.CreatedAt).HasColumnName("created_at").IsRequired();

            e.HasOne<FederatedNode>()
                .WithMany()
                .HasForeignKey(x => x.NodeDomain)
                .OnDelete(DeleteBehavior.Cascade);

            e.HasIndex(x => new { x.NodeDomain, x.OwnerId });
        });
    }
}
