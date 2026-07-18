using Microsoft.EntityFrameworkCore;

namespace Valour.Database;

/// <summary>
/// Bring-your-own-voice configuration for a planet. When enabled, calls in the
/// planet's voice/video channels run on the owner's own LiveKit SFU — media
/// flows DIRECTLY between members and the owner's server, and Valour's only
/// role is signing short-lived join tokens with the owner's API key. Valour
/// never carries, records, or relays the streams. The API secret is encrypted
/// at rest via ASP.NET Data Protection, mirroring <see cref="PlanetStorageConfig"/>.
/// </summary>
public class PlanetVoiceConfig
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
    /// Client-facing LiveKit websocket URL, e.g. wss://voice.example.com.
    /// Members' browsers connect to it directly.
    /// </summary>
    public string LiveKitUrl { get; set; }

    /// <summary>
    /// LiveKit API key (the token issuer id — not secret).
    /// </summary>
    public string ApiKey { get; set; }

    /// <summary>
    /// Data-Protection-encrypted LiveKit API secret used to sign join tokens.
    /// </summary>
    public string ApiSecretEncrypted { get; set; }

    public bool Enabled { get; set; }

    /// <summary>
    /// Last successful RoomService probe against the owner's SFU
    /// </summary>
    public DateTime? VerifiedAt { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }

    public static void SetupDbModel(ModelBuilder builder)
    {
        builder.Entity<PlanetVoiceConfig>(e =>
        {
            e.ToTable("planet_voice_configs");

            e.HasKey(x => x.PlanetId);

            e.Property(x => x.PlanetId)
                .HasColumnName("planet_id");

            e.Property(x => x.LiveKitUrl)
                .HasColumnName("livekit_url")
                .IsRequired();

            e.Property(x => x.ApiKey)
                .HasColumnName("api_key")
                .IsRequired();

            e.Property(x => x.ApiSecretEncrypted)
                .HasColumnName("api_secret_encrypted")
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
                .HasForeignKey<PlanetVoiceConfig>(x => x.PlanetId);
        });
    }
}
