using System.ComponentModel.DataAnnotations.Schema;
using Valour.Shared.Models;

namespace Valour.Database;

[Table("user_subscriptions")]
public class UserSubscription : ISharedUserSubscription
{
    /// <summary>
    /// The id of the subscription
    /// </summary>
    [Column("id")]
    public string Id { get; set; }
    
    /// <summary>
    /// The id of the user who owns this subscription
    /// </summary>
    [Column("user_id")]
    public long UserId { get; set; }
    
    /// <summary>
    /// The type of subscription this represents
    /// </summary>
    [Column("type")]
    public string Type { get; set; }
    
    /// <summary>
    /// The date at which the subscription was created
    /// </summary>
    [Column("created")]
    public DateTime Created { get; set; }
    
    /// <summary>
    /// The last time at which the user was charged for their subscription
    /// </summary>
    [Column("last_charged")]
    public DateTime LastCharged { get; set; }
    
    /// <summary>
    /// If the subscription is currently active.
    /// Subscriptions are not re-activated. A new subscription is created, allowing
    /// subscription lengths to be tracked.
    /// </summary>
    [Column("active")]
    public bool Active { get; set; }
    
    /// <summary>
    /// If a subscription is set to cancelled, it will not be rebilled
    /// </summary>
    [Column("cancelled")]
    public bool Cancelled { get; set; }
    
    /// <summary>
    /// How many times this subscription has been renewed
    /// </summary>
    [Column("renewals")]
    public int Renewals { get; set; }
}