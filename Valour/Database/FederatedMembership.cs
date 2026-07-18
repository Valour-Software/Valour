using Microsoft.EntityFrameworkCore;

namespace Valour.Database;

/// <summary>
/// A hub-side record that a user joined a community-hosted planet. The hub
/// keeps this — before the node ever sees the user — so a node can't fabricate
/// memberships, the client can enumerate "your communities on other servers",
/// and account deletion can reach every node the user touched.
/// </summary>
public class FederatedMembership
{
    public long UserId { get; set; }
    public long PlanetId { get; set; }
    public string NodeDomain { get; set; }
    public DateTime JoinedAt { get; set; }

    public static void SetupDbModel(ModelBuilder builder)
    {
        builder.Entity<FederatedMembership>(e =>
        {
            e.ToTable("federated_memberships");
            e.HasKey(x => new { x.UserId, x.PlanetId });
            e.Property(x => x.UserId).HasColumnName("user_id");
            e.Property(x => x.PlanetId).HasColumnName("planet_id");
            e.Property(x => x.NodeDomain).HasColumnName("node_domain").IsRequired();
            e.Property(x => x.JoinedAt).HasColumnName("joined_at").IsRequired();
            e.HasIndex(x => x.UserId);
            e.HasIndex(x => x.NodeDomain);
        });
    }
}
