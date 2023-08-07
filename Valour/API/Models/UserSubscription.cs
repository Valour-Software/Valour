using Valour.Shared.Models;

namespace Valour.Api.Models;

public class UserSubscription : ISharedUserSubscription
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
    public DateTime Created { get; set; }
    
    /// <summary>
    /// The last time at which the user was charged for their subscription
    /// </summary>
    public DateTime LastCharged { get; set; }
    
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