using Microsoft.EntityFrameworkCore;
using Valour.Shared.Models;

namespace Valour.Database;

public class VillageMapChunk : ISharedVillageMapChunk
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
    /// The map this chunk belongs to
    /// </summary>
    public long MapId { get; set; }

    public int ChunkX { get; set; }

    public int ChunkY { get; set; }

    /// <summary>
    /// Serialized visual layers for this chunk
    /// </summary>
    public string LayerData { get; set; }

    /// <summary>
    /// Serialized per-tile collision for this chunk
    /// </summary>
    public string CollisionData { get; set; }

    public int Version { get; set; }

    public static void SetupDbModel(ModelBuilder builder)
    {
        builder.Entity<VillageMapChunk>(e =>
        {
            // Table

            e.ToTable("village_map_chunks");

            // Key

            e.HasKey(x => x.Id);

            // Properties

            e.Property(x => x.Id)
                .HasColumnName("id");

            e.Property(x => x.PlanetId)
                .HasColumnName("planet_id");

            e.Property(x => x.MapId)
                .HasColumnName("map_id");

            e.Property(x => x.ChunkX)
                .HasColumnName("chunk_x");

            e.Property(x => x.ChunkY)
                .HasColumnName("chunk_y");

            e.Property(x => x.LayerData)
                .HasColumnName("layer_data");

            e.Property(x => x.CollisionData)
                .HasColumnName("collision_data");

            e.Property(x => x.Version)
                .HasColumnName("version");

            // Relationships

            e.HasOne(x => x.Planet)
                .WithMany()
                .HasForeignKey(x => x.PlanetId)
                .OnDelete(DeleteBehavior.Cascade);

            // Indices

            e.HasIndex(x => x.PlanetId);

            e.HasIndex(x => new { x.MapId, x.ChunkX, x.ChunkY })
                .IsUnique();
        });
    }
}
