namespace Valour.Server.Mapping;

public static class WebPushSubscriptionMapper
{
    public static WebPushSubscription ToModel(this Valour.Database.NotificationSubscription subscription)
    {
        if (subscription is null)
            return null;
        
        return new WebPushSubscription()
        {
            Id = subscription.Id,
            UserId = subscription.UserId,
            Endpoint = subscription.Endpoint,
            Key = subscription.Key,
            Auth = subscription.Auth
        };
    }
    
    public static Valour.Database.NotificationSubscription ToDatabase(this WebPushSubscription subscription)
    {
        if (subscription is null)
            return null;
        
        return new Valour.Database.NotificationSubscription()
        {
            Id = subscription.Id,
            UserId = subscription.UserId,
            Endpoint = subscription.Endpoint,
            Key = subscription.Key,
            Auth = subscription.Auth
        };
    }
}