namespace Valour.Server.Mapping;

public static class UserSubscriptionMapper
{
    public static UserSubscription ToModel(this Valour.Database.UserSubscription userSubscription)
    {
        if (userSubscription is null)
            return null;
        
        return new UserSubscription()
        {
            Id = userSubscription.Id,
            UserId = userSubscription.UserId,
            Type = userSubscription.Type,
            Created = userSubscription.Created,
            LastCharged = userSubscription.LastCharged,
            Active = userSubscription.Active,
            Renewals = userSubscription.Renewals
        };
    }
    
    public static Valour.Database.UserSubscription ToDatabase(this UserSubscription userSubscription)
    {
        if (userSubscription is null)
            return null;
        
        return new Valour.Database.UserSubscription()
        {
            Id = userSubscription.Id,
            UserId = userSubscription.UserId,
            Type = userSubscription.Type,
            Created = userSubscription.Created,
            LastCharged = userSubscription.LastCharged,
            Active = userSubscription.Active,
            Renewals = userSubscription.Renewals
        };
    }
}