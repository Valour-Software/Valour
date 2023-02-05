namespace Valour.Server.Mapping;

public static class PlanetChannelMapper
{
    public static PlanetChannel ToModel(this Valour.Database.PlanetChannel channel)
    {
        if (channel is null)
            return null;

        return channel switch
        {
            Valour.Database.PlanetChatChannel chatChannel => chatChannel.ToModel(),
            Valour.Database.PlanetCategory category => category.ToModel(),
            Valour.Database.PlanetVoiceChannel voiceChannel => voiceChannel.ToModel(),
            _ => throw new Exception("Invalid channel type")
        };
    }
    
    public static Valour.Database.PlanetChannel ToDatabase(this PlanetChannel channel)
    {
        if (channel is null)
            return null;

        return channel switch
        {
            PlanetChatChannel chatChannel => chatChannel.ToDatabase(),
            PlanetCategory category => category.ToDatabase(),
            PlanetVoiceChannel voiceChannel => voiceChannel.ToDatabase(),
            _ => throw new Exception("Invalid channel type")
        };
    }
}