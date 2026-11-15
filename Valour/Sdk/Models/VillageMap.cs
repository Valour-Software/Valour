using Valour.Sdk.Client;
using Valour.Sdk.ModelLogic;
using Valour.Shared.Models;

namespace Valour.Sdk.Models;

/// <summary>
/// A single village map: either a planet's outdoor world or the interior of
/// one of its buildings.
/// </summary>
public class VillageMap : ClientPlanetModel<VillageMap, long>, ISharedVillageMap
{
    public override string BaseRoute => ISharedVillageMap.BaseRoute;

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
    public string? AmbientColor { get; set; }

    /// <summary>
    /// Bumped on every content change so clients can discard stale chunks
    /// </summary>
    public int Version { get; set; }

    protected override long? GetPlanetId() => PlanetId;

    [JsonConstructor]
    private VillageMap() : base() { }
    public VillageMap(ValourClient client) : base(client) { }

    // Not cached until its planet is loaded
    protected override ModelStore<VillageMap, long> CacheStore => GetPlanet(false)?.VillageMaps;
}
