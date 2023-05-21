namespace Valour.Server.Mapping;

public static class PlanetChatChannelMapper
{
    public static PlanetChatChannel ToModel(this Valour.Database.PlanetChatChannel channel)
    {
        if (channel is null)
            return null;
        
        return new PlanetChatChannel()
        {
            Id = channel.Id,
            PlanetId = channel.PlanetId,
            Name = channel.Name,
            Position = channel.Position,
            IsDefault = channel.IsDefault,
            Description = channel.Description,
            ParentId = channel.ParentId,
            InheritsPerms = channel.InheritsPerms
        };
    }
    
    public static Valour.Database.PlanetChatChannel ToDatabase(this PlanetChatChannel channel)
    {
        if (channel is null)
            return null;
        
        return new Valour.Database.PlanetChatChannel()
        {
            Id = channel.Id,
            PlanetId = channel.PlanetId,
            Name = channel.Name,
            Position = channel.Position,
            IsDefault = channel.IsDefault,
            Description = channel.Description,
            ParentId = channel.ParentId,
            InheritsPerms = channel.InheritsPerms
        };
    }
}