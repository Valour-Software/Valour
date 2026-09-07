using Valour.Sdk.Client;
using Valour.Sdk.ModelLogic;
using Valour.Shared.Models;

namespace Valour.Sdk.Models;

/// <summary>
/// A structure placed on a village map.
/// </summary>
public class VillageBuilding : ClientPlanetModel<VillageBuilding, long>, ISharedVillageBuilding
{
    public override string BaseRoute => ISharedVillageBuilding.BaseRoute;

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

    public string? Description { get; set; }

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

    public decimal Price { get; set; }

    protected override long? GetPlanetId() => PlanetId;

    [JsonConstructor]
    private VillageBuilding() : base() { }
    public VillageBuilding(ValourClient client) : base(client) { }

    public override VillageBuilding AddToCache(ModelInsertFlags flags = ModelInsertFlags.None)
    {
        var planet = GetPlanet(false);
        if (planet is null)
            return this;

        return planet.VillageBuildings.Put(this, flags);
    }

    public override VillageBuilding RemoveFromCache(bool skipEvents = false)
    {
        return Planet.VillageBuildings.Remove(this, skipEvents);
    }
}
