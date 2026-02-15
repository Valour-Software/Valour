using Microsoft.EntityFrameworkCore;
using Valour.Shared.Models;

namespace Valour.Database;

public class PlanetEmoji : ISharedPlanetEmoji
{
    ///////////////////////////
    // Relational Properties //
    ///////////////////////////

    public Planet Planet { get; set; }
    public User CreatorUser { get; set; }

    ///////////////////////
    // Entity Properties //
    ///////////////////////

    public long Id { get; set; }
    public long PlanetId { get; set; }
    public long CreatorUserId { get; set; }
    public string Name { get; set; }
    public DateTime CreatedAt { get; set; }

    public static void SetupDbModel(ModelBuilder builder)
    {
        builder.Entity<PlanetEmoji>(e =>
        {
            e.ToTable("planet_emojis");

            e.HasKey(x => x.Id);

            e.Property(x => x.Id)
                .HasColumnName("id");

            e.Property(x => x.PlanetId)
                .HasColumnName("planet_id");

            e.Property(x => x.CreatorUserId)
                .HasColumnName("creator_user_id");

            e.Property(x => x.Name)
                .HasColumnName("name")
                .HasMaxLength(32);

            e.Property(x => x.CreatedAt)
                .HasColumnName("created_at");

            e.HasOne(x => x.Planet)
                .WithMany(x => x.Emojis)
                .HasForeignKey(x => x.PlanetId);

            e.HasOne(x => x.CreatorUser)
                .WithMany()
                .HasForeignKey(x => x.CreatorUserId);

            e.HasIndex(x => x.PlanetId);
            e.HasIndex(x => new { x.PlanetId, x.Name })
                .IsUnique();
        });
    }
}
