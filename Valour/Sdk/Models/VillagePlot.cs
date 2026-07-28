using Valour.Sdk.Client;
using Valour.Sdk.ModelLogic;
using Valour.Shared.Models;

namespace Valour.Sdk.Models;

/// <summary>
/// A claimable parcel of land on a village map.
/// </summary>
public class VillagePlot : ClientPlanetModel<VillagePlot, long>, ISharedVillagePlot
{
    public override string BaseRoute => ISharedVillagePlot.BaseRoute;

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

    protected override long? GetPlanetId() => PlanetId;

    [JsonConstructor]
    private VillagePlot() : base() { }
    public VillagePlot(ValourClient client) : base(client) { }

    public override VillagePlot AddToCache(ModelInsertFlags flags = ModelInsertFlags.None)
    {
        var planet = GetPlanet(false);
        if (planet is null)
            return this;

        return planet.VillagePlots.Put(this, flags);
    }

    public override VillagePlot RemoveFromCache(bool skipEvents = false)
    {
        return Planet.VillagePlots.Remove(this, skipEvents);
    }
}
