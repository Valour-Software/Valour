using Microsoft.EntityFrameworkCore;

namespace Valour.Database;

/// <summary>
/// One signed entry in a user's end-to-end encryption key log. The server
/// stores entries in order and verifies them before accepting, but clients
/// verify them again because they do not trust the server.
/// Entries are kept after account deletion so that access logs and messages
/// signed by the account can still be verified by others.
/// </summary>
public class E2eeKeyLogEntry
{
    public long UserId { get; set; }
    public int Seq { get; set; }
    public byte[] Body { get; set; }
    public byte[] Signature { get; set; }
    public DateTime CreatedAt { get; set; }

    public static void SetupDbModel(ModelBuilder builder)
    {
        builder.Entity<E2eeKeyLogEntry>(e =>
        {
            e.ToTable("e2ee_key_log_entries");
            e.HasKey(x => new { x.UserId, x.Seq });
            e.Property(x => x.UserId).HasColumnName("user_id");
            e.Property(x => x.Seq).HasColumnName("seq");
            e.Property(x => x.Body).HasColumnName("body").IsRequired();
            e.Property(x => x.Signature).HasColumnName("signature").IsRequired();
            e.Property(x => x.CreatedAt).HasColumnName("created_at");
        });
    }
}
