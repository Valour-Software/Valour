namespace Valour.Server.Mapping;

public static class VillageBuildingMapper
{
    public static VillageBuilding ToModel(this Valour.Database.VillageBuilding building)
    {
        if (building is null)
            return null;

        return new VillageBuilding()
        {
            Id = building.Id,
            PlanetId = building.PlanetId,
            MapId = building.MapId,
            InteriorMapId = building.InteriorMapId,
            PlotId = building.PlotId,
            ChannelId = building.ChannelId,
            Name = building.Name,
            Description = building.Description,
            X = building.X,
            Y = building.Y,
            Width = building.Width,
            Height = building.Height,
            DoorX = building.DoorX,
            DoorY = building.DoorY,
            SpriteKey = building.SpriteKey,
            OwnerMemberId = building.OwnerMemberId,
            VoiceMode = building.VoiceMode,
            ForSale = building.ForSale,
            Price = building.Price,
        };
    }

    public static Valour.Database.VillageBuilding ToDatabase(this VillageBuilding building)
    {
        if (building is null)
            return null;

        return new Valour.Database.VillageBuilding()
        {
            Id = building.Id,
            PlanetId = building.PlanetId,
            MapId = building.MapId,
            InteriorMapId = building.InteriorMapId,
            PlotId = building.PlotId,
            ChannelId = building.ChannelId,
            Name = building.Name,
            Description = building.Description,
            X = building.X,
            Y = building.Y,
            Width = building.Width,
            Height = building.Height,
            DoorX = building.DoorX,
            DoorY = building.DoorY,
            SpriteKey = building.SpriteKey,
            OwnerMemberId = building.OwnerMemberId,
            VoiceMode = building.VoiceMode,
            ForSale = building.ForSale,
            Price = building.Price,
        };
    }
}
