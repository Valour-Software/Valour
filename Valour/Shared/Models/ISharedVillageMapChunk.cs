namespace Valour.Shared.Models;

/// <summary>
/// A fixed-size square of a village map's tile content. Chunking from the
/// start keeps a large world editable and loadable in pieces; a small interior
/// simply occupies a single chunk.
/// </summary>
public interface ISharedVillageMapChunk : ISharedPlanetModel<long>
{
    public const string BaseRoute = "api/villagemapchunks";

    long MapId { get; set; }

    int ChunkX { get; set; }

    int ChunkY { get; set; }

    /// <summary>
    /// Serialized visual layers for this chunk. Opaque to the server, which
    /// lets the renderer add layers without a schema migration.
    /// </summary>
    string LayerData { get; set; }

    /// <summary>
    /// Serialized per-tile collision for this chunk. Stored rather than derived
    /// so the server can validate movement without loading the tileset.
    /// </summary>
    string CollisionData { get; set; }

    int Version { get; set; }
}
