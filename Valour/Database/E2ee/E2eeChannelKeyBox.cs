using Microsoft.EntityFrameworkCore;

namespace Valour.Database;

/// <summary>
/// A channel key generation sealed to one member's user key by another member.
/// </summary>
public class E2eeChannelKeyBox
{
    public long ChannelId { get; set; }
    public int Generation { get; set; }
    public long UserId { get; set; }
    public int UserKeyGeneration { get; set; }
    public byte[] Box { get; set; }
    public long SharedByUserId { get; set; }
    public DateTime CreatedAt { get; set; }

    public static void SetupDbModel(ModelBuilder builder)
    {
        builder.Entity<E2eeChannelKeyBox>(e =>
        {
            e.ToTable("e2ee_channel_key_boxes");
            e.HasKey(x => new { x.ChannelId, x.Generation, x.UserId });
            e.Property(x => x.ChannelId).HasColumnName("channel_id");
            e.Property(x => x.Generation).HasColumnName("generation");
            e.Property(x => x.UserId).HasColumnName("user_id");
            e.Property(x => x.UserKeyGeneration).HasColumnName("user_key_generation");
            e.Property(x => x.Box).HasColumnName("box").IsRequired();
            e.Property(x => x.SharedByUserId).HasColumnName("shared_by_user_id");
            e.Property(x => x.CreatedAt).HasColumnName("created_at");
            e.HasIndex(x => new { x.UserId, x.ChannelId });
        });
    }
}
