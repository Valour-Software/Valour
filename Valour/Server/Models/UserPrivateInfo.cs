using Valour.Shared.Models;

namespace Valour.Server.Models;

public class UserPrivateInfo : ISharedUserPrivateInfo
{
    public string Email { get; set; }
    public bool Verified { get; set; }
    public long UserId { get; set; }
    public DateTime? BirthDate { get; set; }
    public Locality? Locality { get; set; }
    public string JoinInviteCode { get; set; }
    public string JoinSource { get; set; }
}
