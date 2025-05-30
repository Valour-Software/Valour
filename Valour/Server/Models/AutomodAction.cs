using Valour.Shared.Models.Staff;
using Valour.Shared.Models;

namespace Valour.Server.Models;

public class AutomodAction : ServerModel<Guid>, ISharedAutomodAction
{
public int Strikes { get; set; }
public bool UseGlobalStrikes { get; set; }
    public Guid TriggerId { get; set; }
    public long MemberAddedBy { get; set; }
    public AutomodActionType ActionType { get; set; }
    public long PlanetId { get; set; }
    public long TargetMemberId { get; set; }
    public long? MessageId { get; set; }
    public long? RoleId { get; set; }
    public DateTime? Expires { get; set; }
    public string Message { get; set; }
}
