using Microsoft.EntityFrameworkCore;

namespace Valour.Database;

/// <summary>
/// One signed entry in the membership log of an invite-only planet or a group
/// DM. Members share channel keys only with users this log admits, so the
/// server cannot add anyone on its own.
/// </summary>
public class E2eeAccessLogEntry
{
    /// <summary>1 for planets, 2 for group DM channels.</summary>
    public int Scope { get; set; }

    public long ScopeId { get; set; }
    public int Seq { get; set; }
    public byte[] Body { get; set; }
    public byte[] Signature { get; set; }
    public long SignerUserId { get; set; }

    /// <summary>
    /// True for a checkpoint entry, which records the log's full state. A new
    /// device starts reading from the latest one.
    /// </summary>
    public bool IsCheckpoint { get; set; }

    public DateTime CreatedAt { get; set; }

    public static void SetupDbModel(ModelBuilder builder)
    {
        builder.Entity<E2eeAccessLogEntry>(e =>
        {
            e.ToTable("e2ee_access_log_entries");
            e.HasKey(x => new { x.Scope, x.ScopeId, x.Seq });
            e.Property(x => x.Scope).HasColumnName("scope");
            e.Property(x => x.ScopeId).HasColumnName("scope_id");
            e.Property(x => x.Seq).HasColumnName("seq");
            e.Property(x => x.Body).HasColumnName("body").IsRequired();
            e.Property(x => x.Signature).HasColumnName("signature").IsRequired();
            e.Property(x => x.SignerUserId).HasColumnName("signer_user_id");
            e.Property(x => x.IsCheckpoint).HasColumnName("is_checkpoint");
            e.Property(x => x.CreatedAt).HasColumnName("created_at");
        });
    }
}
