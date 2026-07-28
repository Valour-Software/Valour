namespace Valour.Server.Mapping;

public static class VillageMapMapper
{
    public static VillageMap ToModel(this Valour.Database.VillageMap map)
    {
        if (map is null)
            return null;

        return new VillageMap()
        {
            Id = map.Id,
            PlanetId = map.PlanetId,
            MapType = map.MapType,
            Name = map.Name,
            ParentBuildingId = map.ParentBuildingId,
            Width = map.Width,
            Height = map.Height,
            TileSize = map.TileSize,
            SpawnX = map.SpawnX,
            SpawnY = map.SpawnY,
            TilesetKey = map.TilesetKey,
            AmbientColor = map.AmbientColor,
            Version = map.Version,
        };
    }

    public static Valour.Database.VillageMap ToDatabase(this VillageMap map)
    {
        if (map is null)
            return null;

        return new Valour.Database.VillageMap()
        {
            Id = map.Id,
            PlanetId = map.PlanetId,
            MapType = map.MapType,
            Name = map.Name,
            ParentBuildingId = map.ParentBuildingId,
            Width = map.Width,
            Height = map.Height,
            TileSize = map.TileSize,
            SpawnX = map.SpawnX,
            SpawnY = map.SpawnY,
            TilesetKey = map.TilesetKey,
            AmbientColor = map.AmbientColor,
            Version = map.Version,
        };
    }
}
