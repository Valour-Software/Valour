namespace Valour.Server.Mapping;

public static class NotificationMapper
{
    public static Valour.Api.Models.Notification ToModel(this Valour.Database.Notification notification)
    {
        if (notification is null)
            return null;
        
        return new Valour.Api.Models.Notification()
        {
            Id = notification.Id,
            UserId = notification.UserId,
            PlanetId = notification.PlanetId,
            ChannelId = notification.ChannelId,
            SourceId = notification.SourceId,
            Source = notification.Source
        };
    }
    
    public static Valour.Database.Notification ToDatabase(this Valour.Api.Models.Notification notification)
    {
        if (notification is null)
            return null;
        
        return new Valour.Database.Notification()
        {
            Id = notification.Id,
            UserId = notification.UserId,
            PlanetId = notification.PlanetId,
            ChannelId = notification.ChannelId,
            SourceId = notification.SourceId,
            Source = notification.Source
        };
    }
}