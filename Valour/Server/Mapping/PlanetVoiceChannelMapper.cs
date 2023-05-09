namespace Valour.Server.Mapping;

public static class PlanetVoiceChannelMapper
{
    public static PlanetVoiceChannel ToModel(this Valour.Database.PlanetVoiceChannel channel)
    {
        if (channel is null)
            return null;
        
        return new PlanetVoiceChannel()
        {
            Id = channel.Id,
            PlanetId = channel.PlanetId,
            Name = channel.Name,
            Position = channel.Position,
            Description = channel.Description,
            ParentId = channel.ParentId,
            InheritsPerms = channel.InheritsPerms
        };
    }
    
    public static Valour.Database.PlanetVoiceChannel ToDatabase(this PlanetVoiceChannel channel)
    {
        if (channel is null)
            return null;
        
        return new Valour.Database.PlanetVoiceChannel()
        {
            Id = channel.Id,
            PlanetId = channel.PlanetId,
            Name = channel.Name,
            Position = channel.Position,
            Description = channel.Description,
            ParentId = channel.ParentId,
            InheritsPerms = channel.InheritsPerms
        };
    }
}