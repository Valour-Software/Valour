namespace Valour.Shared.Models;

/// <summary>
/// A single village map: either a planet's outdoor world or the interior of
/// one of its buildings. Tile content lives in chunks rather than on the map
/// itself so large worlds stay cheap to load and edit.
/// </summary>
public interface ISharedVillageMap : ISharedPlanetModel<long>
{
    public const string BaseRoute = "api/villagemaps";
    public const int MaxNameLength = 48;

    /// <summary>
    /// Chunks are a fixed size so a client can resolve which chunks cover its
    /// viewport without consulting the map first.
    /// </summary>
    public const int ChunkSize = 32;

    /// <summary>
    /// The largest map we will accept in either dimension, in tiles.
    /// </summary>
    public const int MaxDimension = 512;

    VillageMapType MapType { get; set; }

    string Name { get; set; }

    /// <summary>
    /// The building this map is the inside of, when this is an interior
    /// </summary>
    long? ParentBuildingId { get; set; }

    int Width { get; set; }

    int Height { get; set; }

    /// <summary>
    /// Edge length of one tile in pixels, as authored
    /// </summary>
    int TileSize { get; set; }

    int SpawnX { get; set; }

    int SpawnY { get; set; }

    /// <summary>
    /// The tileset the map's chunk data is authored against. Tile references
    /// are logical keys into this tileset rather than raw sheet coordinates.
    /// </summary>
    string TilesetKey { get; set; }

    /// <summary>
    /// Optional colour multiplied over the map to tint it, used for interiors
    /// and time-of-day ambience. Null renders unlit.
    /// </summary>
    string AmbientColor { get; set; }

    /// <summary>
    /// Bumped on every content change so clients can discard stale chunks
    /// </summary>
    int Version { get; set; }

    /// <summary>
    /// Number of chunks needed to cover the given dimension.
    /// </summary>
    public static int ChunkCount(int dimension) =>
        dimension <= 0 ? 0 : ((dimension - 1) / ChunkSize) + 1;
}
