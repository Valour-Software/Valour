using IdGen;
using Valour.Server.Database;

namespace Valour.Server.Mapping;

public static class TagMapper
{
    public static Tag ToModel(this Valour.Database.Tag tag)
    {
        if (tag is null)
            return null;
        
        return new Tag
        {
            Id = tag.Id,
            Name = tag.Name,
            Slug = tag.Slug,
            Created = tag.Created
        };
    }
    
    public static Valour.Database.Tag ToDatabase(this Tag tag)
    {
        if (tag is null)
            return null;
        
        return new Valour.Database.Tag()
        {
            Id = IdManager.Generate(),
            Name = tag.Name,
            Slug = tag.Slug,
            Created = DateTime.Today
        };
    }
}