namespace Valour.Shared.Models;

public interface ISharedReferral
{
    long UserId { get; set; }
    long ReferrerId { get; set; }
    DateTime Created { get; set; }
    decimal Reward { get; set; }
}

