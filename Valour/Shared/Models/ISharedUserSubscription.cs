namespace Valour.Shared.Models;

public class UserSubscriptionType
{
    /// <summary>
    /// The name of the subscription type
    /// </summary>
    public string Name { get; set; }

    /// <summary>
    /// The description of the subscription type
    /// </summary>
    public string Description { get; set; }

    /// <summary>
    /// The monthly price of the subscription in Valour Credits
    /// </summary>
    public decimal Price { get; set; }

    /// <summary>
    /// The price in USD cents for Stripe recurring subscription
    /// </summary>
    public long StripePriceCents { get; set; }

    /// <summary>
    /// The Google Play subscription product ID for this tier
    /// </summary>
    public string GooglePlayProductId { get; set; }

    /// <summary>
    /// Monthly VC cashback reward for Stripe and Google Play subscribers
    /// </summary>
    public int VcReward { get; set; }

    /// <summary>
    /// Maximum upload size in bytes for this subscription tier
    /// </summary>
    public long MaxUploadBytes { get; set; }
}

public static class UserSubscriptionTypes
{
    /// <summary>
    /// Default upload limit for users without a subscription (10 MB)
    /// </summary>
    public const long DefaultMaxUploadBytes = 10 * 1024 * 1024;

    public static readonly UserSubscriptionType Stargazer = new()
    {
        Name = "Stargazer",
        GooglePlayProductId = "stargazer",
        Description = "The classic! Support Valour and get access to perks like advanced profile styles.",
        Price = 500,
        StripePriceCents = 499,
        VcReward = 50,
        MaxUploadBytes = 50 * 1024 * 1024, // 50 MB
    };

    public static readonly UserSubscriptionType StargazerPlus = new()
    {
        Name = "Stargazer Plus",
        GooglePlayProductId = "stargazer_plus",
        Description = "A bump up for power users or those who just want to support Valour more.",
        Price = 1000,
        StripePriceCents = 999,
        VcReward = 100,
        MaxUploadBytes = 100 * 1024 * 1024, // 100 MB
    };

    public static readonly UserSubscriptionType StargazerPro = new()
    {
        Name = "Stargazer Pro",
        GooglePlayProductId = "stargazer_pro",
        Description = "Our maximum perks for the most enthusiastic Valournauts!",
        Price = 1500,
        StripePriceCents = 1499,
        VcReward = 150,
        MaxUploadBytes = 250 * 1024 * 1024, // 250 MB
    };

    public static Dictionary<string, UserSubscriptionType> TypeMap = new Dictionary<string, UserSubscriptionType>()
    {
        { "Stargazer", Stargazer },
        { "Stargazer Plus", StargazerPlus },
        { "Stargazer Pro", StargazerPro }
    };

    /// <summary>
    /// Finds the tier sold under a Google Play subscription product ID.
    /// </summary>
    public static UserSubscriptionType FromGooglePlayProductId(string productId) =>
        TypeMap.Values.FirstOrDefault(x => x.GooglePlayProductId == productId);

    /// <summary>
    /// Returns the max upload size in bytes for a given subscription type name.
    /// Returns DefaultMaxUploadBytes if the type is null or unknown.
    /// </summary>
    public static long GetMaxUploadBytes(string typeName)
    {
        if (typeName is not null && TypeMap.TryGetValue(typeName, out var type))
            return type.MaxUploadBytes;
        return DefaultMaxUploadBytes;
    }
}

public interface ISharedUserSubscription
{
    /// <summary>
    /// The id of the subscription
    /// </summary>
    public string Id { get; set; }
    
    /// <summary>
    /// The id of the user who owns this subscription
    /// </summary>
    public long UserId { get; set; }

    
    /// <summary>
    /// The type of subscription this represents
    /// </summary>
    public string Type { get; set; }
    
    /// <summary>
    /// The date at which the subscription was created
    /// </summary>
    DateTime Created { get; set; }
    
    /// <summary>
    /// The last time at which the user was charged for their subscription
    /// </summary>
    DateTime LastCharged { get; set; }
    
    /// <summary>
    /// If the subscription is currently active.
    /// Subscriptions are not re-activated. A new subscription is created, allowing
    /// subscription lengths to be tracked.
    /// </summary>
    public bool Active { get; set; }
    
    /// <summary>
    /// If a subscription is set to cancelled, it will not be rebilled
    /// </summary>
    public bool Cancelled { get; set; }
    
    /// <summary>
    /// How many times this subscription has been renewed
    /// </summary>
    public int Renewals { get; set; }

    /// <summary>
    /// The Stripe subscription ID, if this subscription is managed by Stripe.
    /// Null means the subscription is VC-managed.
    /// </summary>
    public string StripeSubscriptionId { get; set; }

    /// <summary>
    /// Set when the subscription is billed through Google Play. Clients receive
    /// a placeholder rather than the purchase token.
    /// </summary>
    public string GooglePlayPurchaseToken { get; set; }

    /// <summary>
    /// True if the most recent Stripe or Google Play payment attempt failed.
    /// Reset to false when a payment succeeds.
    /// </summary>
    public bool StripePaymentFailed { get; set; }

    /// <summary>
    /// When set, the subscription will change to this tier at the next billing cycle (downgrade).
    /// </summary>
    public string PendingType { get; set; }
}
