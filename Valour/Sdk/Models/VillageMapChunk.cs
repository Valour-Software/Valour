using Valour.Sdk.Client;
using Valour.Sdk.ModelLogic;
using Valour.Shared.Models;

namespace Valour.Sdk.Models;

/// <summary>
/// A fixed-size square of a village map's tile content.
/// </summary>
public class VillageMapChunk : ClientPlanetModel<VillageMapChunk, long>, ISharedVillageMapChunk
{
    public override string BaseRoute => ISharedVillageMapChunk.BaseRoute;

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
    public string? LayerData { get; set; }

    /// <summary>
    /// Serialized per-tile collision for this chunk
    /// </summary>
    public string? CollisionData { get; set; }

    public int Version { get; set; }

    protected override long? GetPlanetId() => PlanetId;

    [JsonConstructor]
    private VillageMapChunk() : base() { }
    public VillageMapChunk(ValourClient client) : base(client) { }

    // Not cached until its planet is loaded
    protected override ModelStore<VillageMapChunk, long> CacheStore => GetPlanet(false)?.VillageMapChunks;
}
