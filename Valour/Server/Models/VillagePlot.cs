using Valour.Shared.Models;

namespace Valour.Server.Models;

public class VillagePlot : ServerModel<long>, ISharedVillagePlot
{
    /// <summary>
    /// The id of the planet this plot belongs to
    /// </summary>
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
    /// Asking price in the planet's currency
    /// </summary>
    public decimal Price { get; set; }
}
