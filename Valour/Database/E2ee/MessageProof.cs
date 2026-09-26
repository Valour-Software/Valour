using Microsoft.EntityFrameworkCore;

namespace Valour.Database;

/// <summary>
/// Proof that an encrypted message revision existed, kept after the message
/// is edited or deleted. It holds the signed header and a hash of the
/// ciphertext, never the text. A recipient who reports the message supplies
/// the text and franking key, and this record lets the server confirm them.
/// Proofs are removed after the configured retention period.
/// </summary>
public class MessageProof
{
    public long MessageId { get; set; }
    public int Revision { get; set; }
    public long ChannelId { get; set; }
    public long? PlanetId { get; set; }
    public long AuthorUserId { get; set; }
    public int EncryptionVersion { get; set; }
    public byte[] Header { get; set; }
    public byte[] BodyHash { get; set; }
    public byte[] Signature { get; set; }
    public DateTime TimeSent { get; set; }
    public DateTime CreatedAt { get; set; }

    public static void SetupDbModel(ModelBuilder builder)
    {
        builder.Entity<MessageProof>(e =>
        {
            e.ToTable("message_proofs");
            e.HasKey(x => new { x.MessageId, x.Revision });
            e.Property(x => x.MessageId).HasColumnName("message_id");
            e.Property(x => x.Revision).HasColumnName("revision");
            e.Property(x => x.ChannelId).HasColumnName("channel_id");
            e.Property(x => x.PlanetId).HasColumnName("planet_id");
            e.Property(x => x.AuthorUserId).HasColumnName("author_user_id");
            e.Property(x => x.EncryptionVersion).HasColumnName("encryption_version");
            e.Property(x => x.Header).HasColumnName("header").IsRequired();
            e.Property(x => x.BodyHash).HasColumnName("body_hash");
            e.Property(x => x.Signature).HasColumnName("signature");
            e.Property(x => x.TimeSent).HasColumnName("time_sent")
                .HasConversion(x => x, x => new DateTime(x.Ticks, DateTimeKind.Utc));
            e.Property(x => x.CreatedAt).HasColumnName("created_at")
                .HasConversion(x => x, x => new DateTime(x.Ticks, DateTimeKind.Utc));
            e.HasIndex(x => x.CreatedAt);

            // Proofs are removed by channel when channels are deleted.
            e.HasIndex(x => x.ChannelId);
        });
    }
}
