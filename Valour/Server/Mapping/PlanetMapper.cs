namespace Valour.Server.Mapping;

public static class PlanetMapper
{
    public static Planet ToModel(this Valour.Database.Planet planet)
    {
        if (planet is null)
            return null;
        
        return new Planet()
        {
            Id = planet.Id,
            OwnerId = planet.OwnerId,
            Name = planet.Name,
            IconUrl = planet.IconUrl,
            Description = planet.Description,
            Public = planet.Public,
            Discoverable = planet.Discoverable,
            DefaultRoleId = planet.DefaultRoleId,
            PrimaryChannelId = planet.PrimaryChannelId
        };
    }
    
    public static Valour.Database.Planet ToDatabase(this Planet planet)
    {
        if (planet is null)
            return null;
        
        return new Valour.Database.Planet()
        {
            Id = planet.Id,
            OwnerId = planet.OwnerId,
            Name = planet.Name,
            IconUrl = planet.IconUrl,
            Description = planet.Description,
            Public = planet.Public,
            Discoverable = planet.Discoverable,
            DefaultRoleId = planet.DefaultRoleId,
            PrimaryChannelId = planet.PrimaryChannelId
        };
    }
}