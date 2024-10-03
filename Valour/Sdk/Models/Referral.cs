using Valour.Shared.Models;

namespace Valour.Sdk.Models;

public class Referral : ISharedReferral
{
    public long UserId { get; set; }
    public long ReferrerId { get; set; }
    public DateTime Created { get; set; }
    public decimal Reward { get; set; }
}

