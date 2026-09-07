using Valour.Shared.Models;

namespace Valour.Server.Models;

public class VillageMapChunk : ServerModel<long>, ISharedVillageMapChunk
{
    /// <summary>
    /// The id of the planet this chunk belongs to
    /// </summary>
    public long PlanetId { get; set; }

    /// <summary>
    /// The map this chunk belongs to
    /// </summary>
    public long MapId { get; set; }

    public int ChunkX { get; set; }

    public int ChunkY { get; set; }

    /// <summary>
    /// Serialized visual layers for this chunk
    /// </summary>
    public string LayerData { get; set; }

    /// <summary>
    /// Serialized per-tile collision for this chunk
    /// </summary>
    public string CollisionData { get; set; }

    public int Version { get; set; }
}
