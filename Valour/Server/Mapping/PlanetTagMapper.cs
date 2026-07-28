using IdGen;
using Valour.Server.Database;

namespace Valour.Server.Mapping;

public static class PlanetTagMapper
{
    public static PlanetTag ToModel(this Valour.Database.PlanetTag planetTag)
    {
        if (planetTag is null)
            return null;
        
        return new PlanetTag
        {
            Id = planetTag.Id,
            Name = planetTag.Name,
            Slug = planetTag.Slug,
            Created = planetTag.Created,
            Curated = planetTag.Curated
        };
    }
    
    public static Valour.Database.PlanetTag ToDatabase(this PlanetTag planetTag)
    {
        if (planetTag is null)
            return null;
        
        // Curated is intentionally NOT copied: only seed data sets it, so a
        // user-created tag can never mark itself curated.
        return new Valour.Database.PlanetTag()
        {
            Id = IdManager.Generate(),
            Name = planetTag.Name,
            Slug = planetTag.Slug,
            Created = DateTime.Today
        };
    }
}