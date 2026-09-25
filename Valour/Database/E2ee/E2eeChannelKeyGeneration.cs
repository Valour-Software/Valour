using Microsoft.EntityFrameworkCore;

namespace Valour.Database;

/// <summary>
/// A signed channel key generation. The secret is never stored here; members
/// receive it sealed to their user key in <see cref="E2eeChannelKeyBox"/>.
/// </summary>
public class E2eeChannelKeyGeneration
{
    public long ChannelId { get; set; }
    public int Generation { get; set; }
    public byte[] Body { get; set; }
    public byte[] Signature { get; set; }
    public long CreatorUserId { get; set; }

    /// <summary>Public key derived from the secret, used to seal server-written messages.</summary>
    public byte[] SealPublicKey { get; set; }

    public int IndexGeneration { get; set; }

    /// <summary>
    /// For a key the server created, the secret while no member holds it yet,
    /// protected with ASP.NET Data Protection. It is cleared as soon as a
    /// member receives the key.
    /// </summary>
    public string HeldSecretProtected { get; set; }

    /// <summary>True once any message has been encrypted or sealed with this generation.</summary>
    public bool HasMessages { get; set; }

    public DateTime CreatedAt { get; set; }

    public static void SetupDbModel(ModelBuilder builder)
    {
        builder.Entity<E2eeChannelKeyGeneration>(e =>
        {
            e.ToTable("e2ee_channel_key_generations");
            e.HasKey(x => new { x.ChannelId, x.Generation });
            e.Property(x => x.ChannelId).HasColumnName("channel_id");
            e.Property(x => x.Generation).HasColumnName("generation");
            e.Property(x => x.Body).HasColumnName("body").IsRequired();
            e.Property(x => x.Signature).HasColumnName("signature").IsRequired();
            e.Property(x => x.CreatorUserId).HasColumnName("creator_user_id");
            e.Property(x => x.SealPublicKey).HasColumnName("seal_public_key").IsRequired();
            e.Property(x => x.IndexGeneration).HasColumnName("index_generation");
            e.Property(x => x.HeldSecretProtected).HasColumnName("held_secret_protected");
            e.Property(x => x.HasMessages).HasColumnName("has_messages");
            e.Property(x => x.CreatedAt).HasColumnName("created_at");
        });
    }
}
