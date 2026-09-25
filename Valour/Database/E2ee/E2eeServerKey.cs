using Microsoft.EntityFrameworkCore;

namespace Valour.Database;

/// <summary>
/// An Ed25519 key a server uses to attest to messages it seals, such as
/// webhook messages and history from before a channel was encrypted. The
/// private key is protected with ASP.NET Data Protection.
///
/// A planet moved from another node brings that node's public keys, so its
/// earlier messages still verify. Those rows have no private key and are
/// never active.
/// </summary>
public class E2eeServerKey
{
    public string Id { get; set; }
    public byte[] PublicKey { get; set; }
    public string PrivateKeyProtected { get; set; }
    public bool Active { get; set; }
    public DateTime CreatedAt { get; set; }

    public static void SetupDbModel(ModelBuilder builder)
    {
        builder.Entity<E2eeServerKey>(e =>
        {
            e.ToTable("e2ee_server_keys");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id").HasMaxLength(64);
            e.Property(x => x.PublicKey).HasColumnName("public_key").IsRequired();
            e.Property(x => x.PrivateKeyProtected).HasColumnName("private_key_protected");
            e.Property(x => x.Active).HasColumnName("active");
            e.Property(x => x.CreatedAt).HasColumnName("created_at");
        });
    }
}
