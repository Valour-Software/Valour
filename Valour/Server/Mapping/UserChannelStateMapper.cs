namespace Valour.Server.Mapping;

public static class UserChannelStateMapper
{
    public static UserChannelState ToModel(this Valour.Database.UserChannelState state)
    {
        if (state is null)
            return null;
        
        return new UserChannelState()
        {
            ChannelId = state.ChannelId,
            UserId = state.UserId,
            LastViewedTime = state.LastViewedTime
        };
    }
    
    public static Valour.Database.UserChannelState ToDatabase(this UserChannelState state)
    {
        if (state is null)
            return null;
        
        return new Valour.Database.UserChannelState()
        {
            ChannelId = state.ChannelId,
            UserId = state.UserId,
            LastViewedTime = state.LastViewedTime
        };
    }
}