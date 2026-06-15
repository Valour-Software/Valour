namespace Valour.Shared.Models;

public interface ISharedUserPrivateInfo
{
    string Email { get; set; }
    bool Verified { get; set; }
    long UserId { get; set; }
    DateTime? BirthDate { get; set; }
}
