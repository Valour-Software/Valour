using Microsoft.EntityFrameworkCore;

namespace Valour.Database;

public enum FederatedNodeStatus
{
    PendingVerification = 0,
    Active = 1,
    Suspended = 2,
}

/// <summary>
/// A community node registered with this hub. Nodes are third-party servers
/// tied to a real domain; the hub mints audience-scoped tokens only for
/// active nodes, and their planets appear in the wider ecosystem.
/// </summary>
public class FederatedNode
{
    ///////////////////////////
    // Relational Properties //
    ///////////////////////////

    public virtual User Owner { get; set; }

    ///////////////////////
    // Entity Properties //
    ///////////////////////

    /// <summary>
    /// The node's domain (e.g. "planets.example.com") — also the token audience
    /// </summary>
    public string Domain { get; set; }

    /// <summary>
    /// The hub account responsible for this node
    /// </summary>
    public long OwnerId { get; set; }

    /// <summary>
    /// The node's public key (JWK JSON) used to verify its server-to-server calls
    /// </summary>
    public string NodePublicJwk { get; set; }

    public FederatedNodeStatus Status { get; set; }

    /// <summary>
    /// Challenge the node must serve at /.well-known/valour-node to prove
    /// domain control. Cleared after verification.
    /// </summary>
    public string VerificationChallenge { get; set; }

    /// <summary>
    /// Node-reported server version from the last heartbeat
    /// </summary>
    public string ReportedVersion { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime? VerifiedAt { get; set; }

    public DateTime? LastSeenAt { get; set; }

    /// <summary>
    /// Explicit node-operator opt-in to receive forward migrations from any
    /// eligible hub planet owner. The safe default is false.
    /// </summary>
    public bool AllowsPublicMigrations { get; set; }

    public static void SetupDbModel(ModelBuilder builder)
    {
        builder.Entity<FederatedNode>(e =>
        {
            e.ToTable("federated_nodes");

            e.HasKey(x => x.Domain);

            e.Property(x => x.Domain)
                .HasColumnName("domain");

            e.Property(x => x.OwnerId)
                .HasColumnName("owner_id")
                .IsRequired();

            e.Property(x => x.NodePublicJwk)
                .HasColumnName("node_public_jwk");

            e.Property(x => x.Status)
                .HasColumnName("status")
                .IsRequired();

            e.Property(x => x.VerificationChallenge)
                .HasColumnName("verification_challenge");

            e.Property(x => x.ReportedVersion)
                .HasColumnName("reported_version");

            e.Property(x => x.CreatedAt)
                .HasColumnName("created_at")
                .IsRequired();

            e.Property(x => x.VerifiedAt)
                .HasColumnName("verified_at");

            e.Property(x => x.LastSeenAt)
                .HasColumnName("last_seen_at");

            e.Property(x => x.AllowsPublicMigrations)
                .HasColumnName("allows_public_migrations")
                .HasDefaultValue(false)
                .IsRequired();

            e.HasOne(x => x.Owner)
                .WithMany()
                .HasForeignKey(x => x.OwnerId);

            e.HasIndex(x => x.OwnerId);
        });
    }
}
