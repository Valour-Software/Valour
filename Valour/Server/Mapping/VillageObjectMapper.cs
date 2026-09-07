namespace Valour.Server.Mapping;

public static class VillageObjectMapper
{
    public static VillageObject ToModel(this Valour.Database.VillageObject obj)
    {
        if (obj is null)
            return null;

        return new VillageObject()
        {
            Id = obj.Id,
            PlanetId = obj.PlanetId,
            MapId = obj.MapId,
            DefinitionKey = obj.DefinitionKey,
            X = obj.X,
            Y = obj.Y,
            Rotation = obj.Rotation,
            ZIndex = obj.ZIndex,
            BlocksMovement = obj.BlocksMovement,
            OwnerMemberId = obj.OwnerMemberId,
        };
    }

    public static Valour.Database.VillageObject ToDatabase(this VillageObject obj)
    {
        if (obj is null)
            return null;

        return new Valour.Database.VillageObject()
        {
            Id = obj.Id,
            PlanetId = obj.PlanetId,
            MapId = obj.MapId,
            DefinitionKey = obj.DefinitionKey,
            X = obj.X,
            Y = obj.Y,
            Rotation = obj.Rotation,
            ZIndex = obj.ZIndex,
            BlocksMovement = obj.BlocksMovement,
            OwnerMemberId = obj.OwnerMemberId,
        };
    }
}
