namespace Valour.Server.Mapping;

public static class ChannelMapper
{
    public static Channel ToModel(this Valour.Database.Channel channel)
    {
        if (channel is null)
            return null;
        
        return new Channel()
        {
            Id = channel.Id
        };
    }
    
    public static Valour.Database.Channel ToDatabase(this Channel channel)
    {
        if (channel is null)
            return null;
        
        return new Valour.Database.Channel()
        {
            Id = channel.Id,
        };
    }
}