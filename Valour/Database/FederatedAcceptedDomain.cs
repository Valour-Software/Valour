using Microsoft.EntityFrameworkCore;

namespace Valour.Database;

/// <summary>
/// Records that a user has accepted a community node's domain — the opt-in list
/// backing the first-contact warning. Stored at the hub so it syncs across the
/// user's devices.
/// </summary>
public class FederatedAcceptedDomain
{
    public long UserId { get; set; }
    public string Domain { get; set; }
    public DateTime AcceptedAt { get; set; }

    public static void SetupDbModel(ModelBuilder builder)
    {
        builder.Entity<FederatedAcceptedDomain>(e =>
        {
            e.ToTable("federated_accepted_domains");
            e.HasKey(x => new { x.UserId, x.Domain });
            e.Property(x => x.UserId).HasColumnName("user_id");
            e.Property(x => x.Domain).HasColumnName("domain");
            e.Property(x => x.AcceptedAt).HasColumnName("accepted_at").IsRequired();
        });
    }
}
