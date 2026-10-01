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
            Cancelled = userSubscription.Cancelled,
            Renewals = userSubscription.Renewals,
            // Do not expose raw Stripe subscription IDs to clients.
            StripeSubscriptionId = string.IsNullOrEmpty(userSubscription.StripeSubscriptionId)
                ? null
                : "stripe_managed",
            GooglePlayPurchaseToken = string.IsNullOrEmpty(userSubscription.GooglePlayPurchaseToken)
                ? null
                : "google_play_managed",
            StripePaymentFailed = userSubscription.StripePaymentFailed,
            PendingType = userSubscription.PendingType
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
            Cancelled = userSubscription.Cancelled,
            Renewals = userSubscription.Renewals,
            StripeSubscriptionId = userSubscription.StripeSubscriptionId,
            GooglePlayPurchaseToken = userSubscription.GooglePlayPurchaseToken,
            StripePaymentFailed = userSubscription.StripePaymentFailed,
            PendingType = userSubscription.PendingType
        };
    }
}
