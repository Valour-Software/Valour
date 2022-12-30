namespace Valour.Server.Mapping;

public static class PlanetCategoryMapper
{
    public static PlanetCategory ToModel(this Valour.Database.PlanetCategory category)
    {
        if (category is null)
            return null;
        
        return new PlanetCategory()
        {
            Id = category.Id,
            TimeLastActive = category.TimeLastActive,
            PlanetId = category.PlanetId,
            Name = category.Name,
            Position = category.Position,
            Description = category.Description,
            ParentId = category.ParentId,
            InheritsPerms = category.InheritsPerms
        };
    }
    
    public static Valour.Database.PlanetCategory ToDatabase(this PlanetCategory category)
    {
        if (category is null)
            return null;
        
        return new Valour.Database.PlanetCategory()
        {
            Id = category.Id,
            TimeLastActive = category.TimeLastActive,
            PlanetId = category.PlanetId,
            Name = category.Name,
            Position = category.Position,
            Description = category.Description,
            ParentId = category.ParentId,
            InheritsPerms = category.InheritsPerms
        };
    }
}
