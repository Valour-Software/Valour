using Microsoft.EntityFrameworkCore;
using Valour.Shared.Models;

namespace Valour.Database;

public class VillageMap : ISharedVillageMap
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
    /// Whether this map is the outdoor world or a building interior
    /// </summary>
    public VillageMapType MapType { get; set; }

    public string Name { get; set; }

    /// <summary>
    /// The building this map is the inside of, when this is an interior
    /// </summary>
    public long? ParentBuildingId { get; set; }

    public int Width { get; set; }

    public int Height { get; set; }

    /// <summary>
    /// Edge length of one tile in pixels, as authored
    /// </summary>
    public int TileSize { get; set; }

    public int SpawnX { get; set; }

    public int SpawnY { get; set; }

    /// <summary>
    /// The tileset the map's chunk data is authored against
    /// </summary>
    public string TilesetKey { get; set; }

    /// <summary>
    /// Optional colour multiplied over the map to tint it
    /// </summary>
    public string AmbientColor { get; set; }

    /// <summary>
    /// Bumped on every content change so clients can discard stale chunks
    /// </summary>
    public int Version { get; set; }

    /// <summary>
    /// Archived interiors stay persisted with their complete contents but are
    /// excluded from navigation and ordinary village queries.
    /// </summary>
    public DateTime? ArchivedAt { get; set; }

    public static void SetupDbModel(ModelBuilder builder)
    {
        builder.Entity<VillageMap>(e =>
        {
            // Table

            e.ToTable("village_maps");

            // Key

            e.HasKey(x => x.Id);

            // Properties

            e.Property(x => x.Id)
                .HasColumnName("id");

            e.Property(x => x.PlanetId)
                .HasColumnName("planet_id");

            e.Property(x => x.MapType)
                .HasColumnName("map_type")
                .HasConversion<int>();

            e.Property(x => x.Name)
                .HasColumnName("name")
                .HasMaxLength(ISharedVillageMap.MaxNameLength);

            e.Property(x => x.ParentBuildingId)
                .HasColumnName("parent_building_id");

            e.Property(x => x.Width)
                .HasColumnName("width");

            e.Property(x => x.Height)
                .HasColumnName("height");

            e.Property(x => x.TileSize)
                .HasColumnName("tile_size");

            e.Property(x => x.SpawnX)
                .HasColumnName("spawn_x");

            e.Property(x => x.SpawnY)
                .HasColumnName("spawn_y");

            e.Property(x => x.TilesetKey)
                .HasColumnName("tileset_key");

            e.Property(x => x.AmbientColor)
                .HasColumnName("ambient_color");

            e.Property(x => x.Version)
                .HasColumnName("version");

            e.Property(x => x.ArchivedAt)
                .HasColumnName("archived_at");

            e.HasQueryFilter(x => x.ArchivedAt == null);

            // Relationships

            e.HasOne(x => x.Planet)
                .WithMany()
                .HasForeignKey(x => x.PlanetId)
                .OnDelete(DeleteBehavior.Cascade);

            // Indices

            e.HasIndex(x => x.PlanetId);
        });
    }
}
