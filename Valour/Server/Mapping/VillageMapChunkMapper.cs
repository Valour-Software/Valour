namespace Valour.Server.Mapping;

public static class VillageMapChunkMapper
{
    public static VillageMapChunk ToModel(this Valour.Database.VillageMapChunk chunk)
    {
        if (chunk is null)
            return null;

        return new VillageMapChunk()
        {
            Id = chunk.Id,
            PlanetId = chunk.PlanetId,
            MapId = chunk.MapId,
            ChunkX = chunk.ChunkX,
            ChunkY = chunk.ChunkY,
            LayerData = chunk.LayerData,
            CollisionData = chunk.CollisionData,
            Version = chunk.Version,
        };
    }

    public static Valour.Database.VillageMapChunk ToDatabase(this VillageMapChunk chunk)
    {
        if (chunk is null)
            return null;

        return new Valour.Database.VillageMapChunk()
        {
            Id = chunk.Id,
            PlanetId = chunk.PlanetId,
            MapId = chunk.MapId,
            ChunkX = chunk.ChunkX,
            ChunkY = chunk.ChunkY,
            LayerData = chunk.LayerData,
            CollisionData = chunk.CollisionData,
            Version = chunk.Version,
        };
    }
}
