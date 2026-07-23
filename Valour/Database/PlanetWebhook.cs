using Microsoft.EntityFrameworkCore;
using Valour.Shared.Models;

namespace Valour.Database;

public class PlanetWebhook : ISharedPlanetWebhook
{
    ///////////////////////////
    // Relational Properties //
    ///////////////////////////

    public Planet Planet { get; set; }

    public Channel Channel { get; set; }

    ///////////////////////
    // Entity Properties //
    ///////////////////////

    public long Id { get; set; }

    public long PlanetId { get; set; }

    /// <summary>
    /// The channel this webhook posts to
    /// </summary>
    public long ChannelId { get; set; }

    /// <summary>
    /// The default display name for messages sent by this webhook
    /// </summary>
    public string Name { get; set; }

    /// <summary>
    /// The default avatar for messages sent by this webhook
    /// </summary>
    public string AvatarUrl { get; set; }

    /// <summary>
    /// The secret token used to execute the webhook
    /// </summary>
    public string Token { get; set; }

    /// <summary>
    /// The user who created the webhook
    /// </summary>
    public long CreatorUserId { get; set; }

    public DateTime TimeCreated { get; set; }

    public static void SetupDbModel(ModelBuilder builder)
    {
        builder.Entity<PlanetWebhook>(e =>
        {
            // Table

            e.ToTable("planet_webhooks");

            // Key

            e.HasKey(x => x.Id);

            // Properties

            e.Property(x => x.Id)
                .HasColumnName("id");

            e.Property(x => x.PlanetId)
                .HasColumnName("planet_id");

            e.Property(x => x.ChannelId)
                .HasColumnName("channel_id");

            e.Property(x => x.Name)
                .HasColumnName("name")
                .HasMaxLength(ISharedPlanetWebhook.MaxNameLength);

            e.Property(x => x.AvatarUrl)
                .HasColumnName("avatar_url")
                .HasMaxLength(ISharedPlanetWebhook.MaxAvatarUrlLength);

            e.Property(x => x.Token)
                .HasColumnName("token")
                .HasMaxLength(64);

            e.Property(x => x.CreatorUserId)
                .HasColumnName("creator_user_id");

            e.Property(x => x.TimeCreated)
                .HasColumnName("time_created")
                .HasConversion(
                    x => x,
                    x => new DateTime(x.Ticks, DateTimeKind.Utc)
                );

            // Relationships

            e.HasOne(x => x.Planet)
                .WithMany()
                .HasForeignKey(x => x.PlanetId)
                .OnDelete(DeleteBehavior.Cascade);

            e.HasOne(x => x.Channel)
                .WithMany()
                .HasForeignKey(x => x.ChannelId)
                .OnDelete(DeleteBehavior.Cascade);

            // Indices

            e.HasIndex(x => x.Token)
                .IsUnique();

            e.HasIndex(x => x.PlanetId);

            e.HasIndex(x => x.ChannelId);
        });
    }
}
