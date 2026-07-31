using Microsoft.EntityFrameworkCore;
using Valour.Shared.Models;

namespace Valour.Database;

public class VillagePlot : ISharedVillagePlot
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
    /// The map this plot sits on
    /// </summary>
    public long MapId { get; set; }

    public string Name { get; set; }

    public int X { get; set; }

    public int Y { get; set; }

    public int Width { get; set; }

    public int Height { get; set; }

    /// <summary>
    /// The member who owns this plot, or null while it is unclaimed
    /// </summary>
    public long? OwnerMemberId { get; set; }

    public VillageEditMode EditMode { get; set; }

    /// <summary>
    /// True while the plot is listed for sale
    /// </summary>
    public bool ForSale { get; set; }

    /// <summary>
    /// Stable identity of the current listing. It survives retries of a
    /// partially completed purchase and changes when the property is relisted.
    /// </summary>
    public string SaleId { get; set; }

    /// <summary>
    /// Asking price in the planet's currency
    /// </summary>
    public decimal Price { get; set; }

    public static void SetupDbModel(ModelBuilder builder)
    {
        builder.Entity<VillagePlot>(e =>
        {
            // Table

            e.ToTable("village_plots");

            // Key

            e.HasKey(x => x.Id);

            // Properties

            e.Property(x => x.Id)
                .HasColumnName("id");

            e.Property(x => x.PlanetId)
                .HasColumnName("planet_id");

            e.Property(x => x.MapId)
                .HasColumnName("map_id");

            e.Property(x => x.Name)
                .HasColumnName("name")
                .HasMaxLength(ISharedVillagePlot.MaxNameLength);

            e.Property(x => x.X)
                .HasColumnName("x");

            e.Property(x => x.Y)
                .HasColumnName("y");

            e.Property(x => x.Width)
                .HasColumnName("width");

            e.Property(x => x.Height)
                .HasColumnName("height");

            e.Property(x => x.OwnerMemberId)
                .HasColumnName("owner_member_id");

            e.Property(x => x.EditMode)
                .HasColumnName("edit_mode")
                .HasConversion<int>();

            e.Property(x => x.ForSale)
                .HasColumnName("for_sale");

            e.Property(x => x.SaleId)
                .HasColumnName("sale_id")
                .HasMaxLength(32);

            e.Property(x => x.Price)
                .HasColumnName("price")
                .HasColumnType("numeric");

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
