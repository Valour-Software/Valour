namespace Valour.Server.Mapping;

public static class DirectChatChannelMapper
{
    public static DirectChatChannel ToModel(this Valour.Database.DirectChatChannel channel)
    {
        if (channel is null)
            return null;
        
        return new DirectChatChannel()
        {
            Id = channel.Id,
            UserOneId = channel.UserOneId,
            UserTwoId = channel.UserTwoId
        };
    }
    
    public static Valour.Database.DirectChatChannel ToDatabase(this DirectChatChannel channel)
    {
        if (channel is null)
            return null;
        
        return new Valour.Database.DirectChatChannel()
        {
            Id = channel.Id,
            UserOneId = channel.UserOneId,
            UserTwoId = channel.UserTwoId
        };
    }
}