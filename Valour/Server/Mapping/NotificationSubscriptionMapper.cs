namespace Valour.Server.Mapping;

public static class NotificationSubscriptionMapper
{
    public static PushNotificationSubscription ToModel(this Valour.Database.PushNotificationSubscription subscription)
    {
        if (subscription is null)
            return null;
        
        return new PushNotificationSubscription()
        {
            Id = subscription.Id,
            UserId = subscription.UserId,
            PlanetId = subscription.PlanetId,
            MemberId = subscription.MemberId,
            RoleHashKey = subscription.RoleHashKey,
            Endpoint = subscription.Endpoint,
            Key = subscription.Key,
            Auth = subscription.Auth,
            ExpiresAt = subscription.ExpiresAt
        };
    }
    
    public static Valour.Database.PushNotificationSubscription ToDatabase(this PushNotificationSubscription subscription)
    {
        if (subscription is null)
            return null;
        
        return new Valour.Database.PushNotificationSubscription()
        {
            Id = subscription.Id,
            UserId = subscription.UserId,
            PlanetId = subscription.PlanetId,
            MemberId = subscription.MemberId,
            RoleHashKey = subscription.RoleHashKey,
            Endpoint = subscription.Endpoint,
            Key = subscription.Key,
            Auth = subscription.Auth,
            ExpiresAt = subscription.ExpiresAt
        };
    }
}