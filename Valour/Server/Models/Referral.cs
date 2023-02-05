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
}
