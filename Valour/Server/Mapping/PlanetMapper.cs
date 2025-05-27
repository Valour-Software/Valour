using Valour.Database;
using Valour.Server.Database;
using Valour.Shared.Models;
using Planet = Valour.Server.Models.Planet;
using Tag = Valour.Server.Models.Tag;

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
            HasCustomIcon = planet.HasCustomIcon,
            HasAnimatedIcon = planet.HasAnimatedIcon,
            Description = planet.Description,
            Public = planet.Public,
            Discoverable = planet.Discoverable,
            Nsfw = planet.Nsfw,
            Version = planet.Version,
            TagId = planet.Tags?.Select(t => t.Id).ToList()
        };
    }
    
    public static Valour.Database.Planet ToDatabase(
        this Planet planet, 
        Valour.Database.Planet existingDbPlanet = null)
    {
        if (planet is null)
            return null;

        var dbPlanet = existingDbPlanet ?? new Valour.Database.Planet();

        dbPlanet.Id = planet.Id;
        dbPlanet.OwnerId = planet.OwnerId;
        dbPlanet.Name = planet.Name;
        dbPlanet.HasCustomIcon = planet.HasCustomIcon;
        dbPlanet.HasAnimatedIcon = planet.HasAnimatedIcon;
        dbPlanet.Description = planet.Description;
        dbPlanet.Public = planet.Public;
        dbPlanet.Discoverable = planet.Discoverable;
        dbPlanet.Nsfw = planet.Nsfw;
        dbPlanet.Version = planet.Version;

        return dbPlanet;
    }
}