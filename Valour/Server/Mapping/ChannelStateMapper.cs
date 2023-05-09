namespace Valour.Server.Mapping;

public static class ChannelStateMapper
{
    public static ChannelState ToModel(this Valour.Database.ChannelState state)
    {
        if (state is null)
            return null;
        
        return new ChannelState()
        {
            ChannelId = state.ChannelId,
            PlanetId = state.PlanetId,
            LastUpdateTime = state.LastUpdateTime,
        };
    }
    
    public static Valour.Database.ChannelState ToDatabase(this ChannelState state)
    {
        if (state is null)
            return null;
        
        return new Valour.Database.ChannelState()
        {
            ChannelId = state.ChannelId,
            PlanetId = state.PlanetId,
            LastUpdateTime = state.LastUpdateTime,
        };
    }
}