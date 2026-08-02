using Microsoft.EntityFrameworkCore;
using Valour.Shared.Models;

namespace Valour.Database;

public class VillageBuilding : ISharedVillageBuilding
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
    /// The map this building stands on
    /// </summary>
    public long MapId { get; set; }

    /// <summary>
    /// The interior this building leads into, if it can be entered
    /// </summary>
    public long? InteriorMapId { get; set; }

    /// <summary>
    /// The plot this building stands on, if any
    /// </summary>
    public long? PlotId { get; set; }

    /// <summary>
    /// A chat or voice channel surfaced by this building
    /// </summary>
    public long? ChannelId { get; set; }

    public string Name { get; set; }

    public string Description { get; set; }

    public int X { get; set; }

    public int Y { get; set; }

    public int Width { get; set; }

    public int Height { get; set; }

    /// <summary>
    /// Tile the member must step onto to enter
    /// </summary>
    public int DoorX { get; set; }

    public int DoorY { get; set; }

    /// <summary>
    /// Logical sprite key into the map's tileset
    /// </summary>
    public string SpriteKey { get; set; }

    public long? OwnerMemberId { get; set; }

    public VillageVoiceMode VoiceMode { get; set; }

    public bool ForSale { get; set; }

    /// <summary>
    /// Stable identity of the current listing. It survives retries of a
    /// partially completed purchase and changes when the property is relisted.
    /// </summary>
    public string SaleId { get; set; }

    public decimal Price { get; set; }

    /// <summary>
    /// Soft-deleted structures retain their interior and furnishings for
    /// moderation, restoration, and future inventory workflows.
    /// </summary>
    public DateTime? ArchivedAt { get; set; }

    public static void SetupDbModel(ModelBuilder builder)
    {
        builder.Entity<VillageBuilding>(e =>
        {
            // Table

            e.ToTable("village_buildings");

            // Key

            e.HasKey(x => x.Id);

            // Properties

            e.Property(x => x.Id)
                .HasColumnName("id");

            e.Property(x => x.PlanetId)
                .HasColumnName("planet_id");

            e.Property(x => x.MapId)
                .HasColumnName("map_id");

            e.Property(x => x.InteriorMapId)
                .HasColumnName("interior_map_id");

            e.Property(x => x.PlotId)
                .HasColumnName("plot_id");

            e.Property(x => x.ChannelId)
                .HasColumnName("channel_id");

            e.Property(x => x.Name)
                .HasColumnName("name")
                .HasMaxLength(ISharedVillageBuilding.MaxNameLength);

            e.Property(x => x.Description)
                .HasColumnName("description")
                .HasMaxLength(ISharedVillageBuilding.MaxDescriptionLength);

            e.Property(x => x.X)
                .HasColumnName("x");

            e.Property(x => x.Y)
                .HasColumnName("y");

            e.Property(x => x.Width)
                .HasColumnName("width");

            e.Property(x => x.Height)
                .HasColumnName("height");

            e.Property(x => x.DoorX)
                .HasColumnName("door_x");

            e.Property(x => x.DoorY)
                .HasColumnName("door_y");

            e.Property(x => x.SpriteKey)
                .HasColumnName("sprite_key");

            e.Property(x => x.OwnerMemberId)
                .HasColumnName("owner_member_id");

            e.Property(x => x.VoiceMode)
                .HasColumnName("voice_mode")
                .HasConversion<int>();

            e.Property(x => x.ForSale)
                .HasColumnName("for_sale");

            e.Property(x => x.SaleId)
                .HasColumnName("sale_id")
                .HasMaxLength(32);

            e.Property(x => x.Price)
                .HasColumnName("price")
                .HasColumnType("numeric");

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

            e.HasIndex(x => x.MapId);
        });
    }
}
