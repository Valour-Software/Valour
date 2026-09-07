using Microsoft.EntityFrameworkCore;
using Valour.Shared.Models;

namespace Valour.Database;

public class VillageObject : ISharedVillageObject
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

    /// <summary>
    /// The map this object is placed on
    /// </summary>
    public long MapId { get; set; }

    /// <summary>
    /// Logical sprite key into the map's tileset
    /// </summary>
    public string DefinitionKey { get; set; }

    public int X { get; set; }

    public int Y { get; set; }

    /// <summary>
    /// Rotation in 90 degree steps
    /// </summary>
    public int Rotation { get; set; }

    /// <summary>
    /// Tie-breaker for objects sharing a tile row
    /// </summary>
    public int ZIndex { get; set; }

    public bool BlocksMovement { get; set; }

    public long? OwnerMemberId { get; set; }

    public static void SetupDbModel(ModelBuilder builder)
    {
        builder.Entity<VillageObject>(e =>
        {
            // Table

            e.ToTable("village_objects");

            // Key

            e.HasKey(x => x.Id);

            // Properties

            e.Property(x => x.Id)
                .HasColumnName("id");

            e.Property(x => x.PlanetId)
                .HasColumnName("planet_id");

            e.Property(x => x.MapId)
                .HasColumnName("map_id");

            e.Property(x => x.DefinitionKey)
                .HasColumnName("definition_key");

            e.Property(x => x.X)
                .HasColumnName("x");

            e.Property(x => x.Y)
                .HasColumnName("y");

            e.Property(x => x.Rotation)
                .HasColumnName("rotation");

            e.Property(x => x.ZIndex)
                .HasColumnName("z_index");

            e.Property(x => x.BlocksMovement)
                .HasColumnName("blocks_movement");

            e.Property(x => x.OwnerMemberId)
                .HasColumnName("owner_member_id");

            // Relationships

            e.HasOne(x => x.Planet)
                .WithMany()
                .HasForeignKey(x => x.PlanetId)
                .OnDelete(DeleteBehavior.Cascade);

            // Indices

            e.HasIndex(x => x.PlanetId);

            e.HasIndex(x => x.MapId);
        });
    }
}
