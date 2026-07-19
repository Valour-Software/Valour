using Microsoft.EntityFrameworkCore;

namespace Valour.Database;

/// <summary>
/// A hub-issued capability authorizing one named hub account to join one
/// community-hosted planet. The signed capability is carried by the client;
/// this row is the hub's revocation and reconciliation record, and is also
/// cached by the destination node after an offline redemption.
/// </summary>
public class FederatedInviteGrant
{
    /// <summary>Cryptographically-random JWT id, not a user-visible sequence.</summary>
    public string Id { get; set; }

    public long PlanetId { get; set; }
    public string NodeDomain { get; set; }
    public long CreatorUserId { get; set; }
    public long IntendedUserId { get; set; }
    public int MaxUses { get; set; }
    public int Uses { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime ExpiresAt { get; set; }
    public DateTime? RevokedAt { get; set; }

    public static void SetupDbModel(ModelBuilder builder)
    {
        builder.Entity<FederatedInviteGrant>(e =>
        {
            e.ToTable("federated_invite_grants");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id");
            e.Property(x => x.PlanetId).HasColumnName("planet_id");
            e.Property(x => x.NodeDomain).HasColumnName("node_domain").IsRequired();
            e.Property(x => x.CreatorUserId).HasColumnName("creator_user_id");
            e.Property(x => x.IntendedUserId).HasColumnName("intended_user_id");
            e.Property(x => x.MaxUses).HasColumnName("max_uses");
            e.Property(x => x.Uses).HasColumnName("uses");
            e.Property(x => x.CreatedAt).HasColumnName("created_at").IsRequired();
            e.Property(x => x.ExpiresAt).HasColumnName("expires_at").IsRequired();
            e.Property(x => x.RevokedAt).HasColumnName("revoked_at");
            e.HasIndex(x => x.PlanetId);
            e.HasIndex(x => x.NodeDomain);
            e.HasIndex(x => x.IntendedUserId);
        });
    }
}
