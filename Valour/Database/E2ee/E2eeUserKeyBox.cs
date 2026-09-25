using Microsoft.EntityFrameworkCore;

namespace Valour.Database;

/// <summary>
/// A user key generation sealed to one of the user's devices or to the user's
/// recovery key. Only that device or recovery code can open it.
/// </summary>
public class E2eeUserKeyBox
{
    public long UserId { get; set; }
    public int Generation { get; set; }

    /// <summary>Device ID or recovery ID the box is sealed to.</summary>
    public string RecipientId { get; set; }

    public byte[] Box { get; set; }
    public DateTime CreatedAt { get; set; }

    public static void SetupDbModel(ModelBuilder builder)
    {
        builder.Entity<E2eeUserKeyBox>(e =>
        {
            e.ToTable("e2ee_user_key_boxes");
            e.HasKey(x => new { x.UserId, x.Generation, x.RecipientId });
            e.Property(x => x.UserId).HasColumnName("user_id");
            e.Property(x => x.Generation).HasColumnName("generation");
            e.Property(x => x.RecipientId).HasColumnName("recipient_id").HasMaxLength(64);
            e.Property(x => x.Box).HasColumnName("box").IsRequired();
            e.Property(x => x.CreatedAt).HasColumnName("created_at");
        });
    }
}
