using Microsoft.EntityFrameworkCore;
using Valour.Shared.Models;

namespace Valour.Database;

public class PlanetRule : ISharedPlanetRule
{
    ///////////////////////////
    // Relational Properties //
    ///////////////////////////

    public Planet Planet { get; set; }

    ///////////////////////
    // Entity Properties //
    ///////////////////////

    public long Id { get; set; }
    public long PlanetId { get; set; }
    public uint Position { get; set; }
    public string Title { get; set; }
    public string Description { get; set; }

    public static void SetupDbModel(ModelBuilder builder)
    {
        builder.Entity<PlanetRule>(e =>
        {
            e.ToTable("planet_rules");

            e.HasKey(x => x.Id);

            e.Property(x => x.Id)
                .HasColumnName("id");

            e.Property(x => x.PlanetId)
                .HasColumnName("planet_id");

            e.Property(x => x.Position)
                .HasColumnName("position");

            e.Property(x => x.Title)
                .HasColumnName("title")
                .HasMaxLength(ISharedPlanetRule.MaxTitleLength);

            e.Property(x => x.Description)
                .HasColumnName("description")
                .HasMaxLength(ISharedPlanetRule.MaxDescriptionLength);

            e.HasOne(x => x.Planet)
                .WithMany(x => x.Rules)
                .HasForeignKey(x => x.PlanetId)
                .OnDelete(DeleteBehavior.Cascade);

            e.HasIndex(x => x.PlanetId);
            e.HasIndex(x => new { x.PlanetId, x.Position });
            e.HasIndex(x => new { x.PlanetId, x.Id })
                .IsUnique();
        });
    }
}
