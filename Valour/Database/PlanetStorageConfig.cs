using Microsoft.EntityFrameworkCore;

namespace Valour.Database;

/// <summary>
/// Bring-your-own-storage configuration for a planet. When enabled, message
/// media for the planet is uploaded by clients DIRECTLY to the owner's
/// S3-compatible bucket and served from the owner's public base URL — Valour
/// never receives, stores, scans, or serves the bytes. Credentials are
/// encrypted at rest via ASP.NET Data Protection.
/// </summary>
public class PlanetStorageConfig
{
    ///////////////////////////
    // Relational Properties //
    ///////////////////////////

    public virtual Planet Planet { get; set; }

    ///////////////////////
    // Entity Properties //
    ///////////////////////

    public long PlanetId { get; set; }

    /// <summary>
    /// S3-compatible API endpoint, e.g. https://s3.example.com
    /// </summary>
    public string Endpoint { get; set; }

    public string Bucket { get; set; }

    public string Region { get; set; }

    /// <summary>
    /// Data-Protection-encrypted access key
    /// </summary>
    public string AccessKeyEncrypted { get; set; }

    /// <summary>
    /// Data-Protection-encrypted secret key
    /// </summary>
    public string SecretKeyEncrypted { get; set; }

    /// <summary>
    /// Public base URL media is served from (bucket public URL or the owner's
    /// CDN domain), e.g. https://media.example.com. Attachment locations for
    /// this planet must be under this base.
    /// </summary>
    public string PublicBaseUrl { get; set; }

    public bool Enabled { get; set; }

    /// <summary>
    /// Last successful write/read/delete probe against the bucket
    /// </summary>
    public DateTime? VerifiedAt { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }

    public static void SetupDbModel(ModelBuilder builder)
    {
        builder.Entity<PlanetStorageConfig>(e =>
        {
            e.ToTable("planet_storage_configs");

            e.HasKey(x => x.PlanetId);

            e.Property(x => x.PlanetId)
                .HasColumnName("planet_id");

            e.Property(x => x.Endpoint)
                .HasColumnName("endpoint")
                .IsRequired();

            e.Property(x => x.Bucket)
                .HasColumnName("bucket")
                .IsRequired();

            e.Property(x => x.Region)
                .HasColumnName("region");

            e.Property(x => x.AccessKeyEncrypted)
                .HasColumnName("access_key_encrypted")
                .IsRequired();

            e.Property(x => x.SecretKeyEncrypted)
                .HasColumnName("secret_key_encrypted")
                .IsRequired();

            e.Property(x => x.PublicBaseUrl)
                .HasColumnName("public_base_url")
                .IsRequired();

            e.Property(x => x.Enabled)
                .HasColumnName("enabled")
                .IsRequired();

            e.Property(x => x.VerifiedAt)
                .HasColumnName("verified_at");

            e.Property(x => x.CreatedAt)
                .HasColumnName("created_at")
                .IsRequired();

            e.Property(x => x.UpdatedAt)
                .HasColumnName("updated_at")
                .IsRequired();

            e.HasOne(x => x.Planet)
                .WithOne()
                .HasForeignKey<PlanetStorageConfig>(x => x.PlanetId);
        });
    }
}
