using Microsoft.EntityFrameworkCore;

namespace Valour.Database;

/// <summary>
/// Durable node-side ledger for offline invite redemption. The signed
/// passport and proof are retained only until the hub acknowledges the
/// redemption, allowing delayed reconciliation without trusting node claims.
/// </summary>
public class FederatedInviteRedemption
{
    public string GrantId { get; set; }
    public long UserId { get; set; }
    public long PlanetId { get; set; }
    public DateTime RedeemedAt { get; set; }
    public string Passport { get; set; }
    public string Proof { get; set; }
    public DateTime? ReportedAt { get; set; }
    public DateTime? RejectedAt { get; set; }
    public string RejectionReason { get; set; }

    public static void SetupDbModel(ModelBuilder builder)
    {
        builder.Entity<FederatedInviteRedemption>(e =>
        {
            e.ToTable("federated_invite_redemptions");
            e.HasKey(x => new { x.GrantId, x.UserId });
            e.Property(x => x.GrantId).HasColumnName("grant_id");
            e.Property(x => x.UserId).HasColumnName("user_id");
            e.Property(x => x.PlanetId).HasColumnName("planet_id");
            e.Property(x => x.RedeemedAt).HasColumnName("redeemed_at").IsRequired();
            e.Property(x => x.Passport).HasColumnName("passport");
            e.Property(x => x.Proof).HasColumnName("proof");
            e.Property(x => x.ReportedAt).HasColumnName("reported_at");
            e.Property(x => x.RejectedAt).HasColumnName("rejected_at");
            e.Property(x => x.RejectionReason).HasColumnName("rejection_reason");
            e.HasIndex(x => x.ReportedAt);
            e.HasIndex(x => x.PlanetId);
        });
    }
}
