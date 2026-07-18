using Microsoft.EntityFrameworkCore;

namespace Valour.Database;

/// <summary>
/// A hub federation signing key. The private key is Data-Protection-encrypted;
/// the public half is published as a JWKS at /.well-known/valour-federation so
/// community nodes can verify hub-minted tokens offline.
/// </summary>
public class FederationKey
{
    /// <summary>
    /// Key id (kid) — referenced in token headers for rotation
    /// </summary>
    public string Id { get; set; }

    /// <summary>
    /// What this key signs: "hub" (federation tokens minted for nodes, public
    /// half published in JWKS) or "node" (this node's own S2S request tokens).
    /// </summary>
    public string Purpose { get; set; }

    /// <summary>
    /// JOSE algorithm, e.g. "ES256"
    /// </summary>
    public string Algorithm { get; set; }

    /// <summary>
    /// Public key as a JWK JSON object
    /// </summary>
    public string PublicJwk { get; set; }

    /// <summary>
    /// Data-Protection-encrypted PKCS#8 private key
    /// </summary>
    public string PrivateKeyProtected { get; set; }

    public bool Active { get; set; }

    public DateTime CreatedAt { get; set; }

    public static void SetupDbModel(ModelBuilder builder)
    {
        builder.Entity<FederationKey>(e =>
        {
            e.ToTable("federation_keys");

            e.HasKey(x => x.Id);

            e.Property(x => x.Id)
                .HasColumnName("id");

            e.Property(x => x.Purpose)
                .HasColumnName("purpose")
                .HasDefaultValue("hub")
                .IsRequired();

            e.Property(x => x.Algorithm)
                .HasColumnName("algorithm")
                .IsRequired();

            e.Property(x => x.PublicJwk)
                .HasColumnName("public_jwk")
                .IsRequired();

            e.Property(x => x.PrivateKeyProtected)
                .HasColumnName("private_key_protected")
                .IsRequired();

            e.Property(x => x.Active)
                .HasColumnName("active")
                .IsRequired();

            e.Property(x => x.CreatedAt)
                .HasColumnName("created_at")
                .IsRequired();
        });
    }
}
