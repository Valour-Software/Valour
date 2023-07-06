namespace Valour.Server.Mapping;

public static class NotificationMapper
{
    public static Notification ToModel(this Valour.Database.Notification notification)
    {
        if (notification is null)
            return null;
        
        return new Notification()
        {
            Id = notification.Id,
            UserId = notification.UserId,
            PlanetId = notification.PlanetId,
            ChannelId = notification.ChannelId,
            SourceId = notification.SourceId,
            Source = notification.Source,
            TimeSent = notification.TimeSent,
            TimeRead = notification.TimeRead,
            Title = notification.Title,
            Body = notification.Body,
            ImageUrl = notification.ImageUrl,
            ClickUrl = notification.ClickUrl
        };
    }
    
    public static Valour.Database.Notification ToDatabase(this Notification notification)
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
            Source = notification.Source,
            TimeSent = notification.TimeSent,
            TimeRead = notification.TimeRead,
            Title = notification.Title,
            Body = notification.Body,
            ImageUrl = notification.ImageUrl,
            ClickUrl = notification.ClickUrl
        };
    }
}