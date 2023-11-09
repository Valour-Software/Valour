namespace Valour.Server.Mapping;

public static class ChannelMapper
{
    public static Channel ToModel(this Valour.Database.Channel channel)
    {
        if (channel is null)
            return null;
        
        return new Channel()
        {
            Id = channel.Id,
            Name = channel.Name,
            Description = channel.Description,
            ChannelType = channel.ChannelType,
            LastUpdateTime = channel.LastUpdateTime,
            PlanetId = channel.PlanetId,
            ParentId = channel.ParentId,
            Position = channel.Position,
            InheritsPerms = channel.InheritsPerms,
            IsDefault = channel.IsDefault,
            
            Members = channel.Members?.Select(x => x.ToModel()).ToList()
        };
    }
    
    public static Valour.Database.Channel ToDatabase(this Channel channel)
    {
        if (channel is null)
            return null;
        
        return new Valour.Database.Channel()
        {
            Id = channel.Id,
            Name = channel.Name,
            Description = channel.Description,
            ChannelType = channel.ChannelType,
            LastUpdateTime = channel.LastUpdateTime,
            PlanetId = channel.PlanetId,
            ParentId = channel.ParentId,
            Position = channel.Position,
            InheritsPerms = channel.InheritsPerms,
            IsDefault = channel.IsDefault,
            
            Members = channel.Members?.Select(x => x.ToDatabase()).ToList()
        };
    }
}