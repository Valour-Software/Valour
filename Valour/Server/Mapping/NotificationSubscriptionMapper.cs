using Valour.Api.Models;

namespace Valour.Server.Mapping;

public static class NotificationSubscriptionMapper
{
    public static NotificationSubscription ToModel(this Valour.Database.NotificationSubscription subscription)
    {
        if (subscription is null)
            return null;
        
        return new NotificationSubscription()
        {
            Id = subscription.Id,
            UserId = subscription.UserId,
            Endpoint = subscription.Endpoint,
            Key = subscription.Key,
            Auth = subscription.Auth
        };
    }
    
    public static Valour.Database.NotificationSubscription ToDatabase(this NotificationSubscription subscription)
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