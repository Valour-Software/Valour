namespace Valour.Shared.Models;

public class SubscriptionType
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
}

public static class SubscriptionTypes
{
    public static readonly SubscriptionType Stargazer = new()
    {
        Name = "Stargazer",
        Description = "Looking above, curious yet confident. Stargazers support Valour and get access to perks like advanced profile styles.",
        Price = 400
    };
    
    public static readonly SubscriptionType Pioneer = new()
    {
        Name = "Pioneer",
        Description = "The next step, exploring the stars and learning. <PLACEHOLDER>",
        Price = 800
    };
    
    public static readonly SubscriptionType Guardian = new()
    {
        Name = "Guardian",
        Description = "Respected, defending the frontier for those who follow. <PLACEHOLDER>",
        Price = 1500
    };
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
    /// How many times this subscription has been renewed
    /// </summary>
    public int Renewals { get; set; }
}