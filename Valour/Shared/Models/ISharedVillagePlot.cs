namespace Valour.Shared.Models;

/// <summary>
/// A claimable parcel of land on a village map. Plots are the unit of
/// ownership and sale, and gate who may build on the ground they cover.
/// </summary>
public interface ISharedVillagePlot : ISharedPlanetModel<long>
{
    public const string BaseRoute = "api/villageplots";
    public const int MaxNameLength = 48;

    long MapId { get; set; }

    string Name { get; set; }

    int X { get; set; }

    int Y { get; set; }

    int Width { get; set; }

    int Height { get; set; }

    /// <summary>
    /// The member who owns this plot, or null while it is unclaimed
    /// </summary>
    long? OwnerMemberId { get; set; }

    VillageEditMode EditMode { get; set; }

    /// <summary>
    /// True while the plot is listed for sale
    /// </summary>
    bool ForSale { get; set; }

    /// <summary>
    /// Asking price in the planet's currency. Zero means free to claim.
    /// </summary>
    decimal Price { get; set; }

    /// <summary>
    /// Returns true when the tile falls inside the plot's bounds.
    /// </summary>
    public static bool Contains(ISharedVillagePlot plot, int x, int y) =>
        x >= plot.X && x < plot.X + plot.Width &&
        y >= plot.Y && y < plot.Y + plot.Height;
}
