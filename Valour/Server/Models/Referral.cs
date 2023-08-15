using Valour.Shared.Models;

namespace Valour.Server.Models;

public class Referral : ISharedReferral
{
    /// <summary>
    /// The id of the user that was referred
    /// </summary>
    public long UserId { get; set; }
    
    /// <summary>
    /// The id of the user that made the referral
    /// </summary>
    public long ReferrerId { get; set; }
    
    /// <summary>
    /// The time and date that the referral was made
    /// </summary>
    public DateTime Created { get; set; }
    
    /// <summary>
    /// The reward given to the referrer for making the referral
    /// </summary>
    public decimal Reward { get; set; }
}
