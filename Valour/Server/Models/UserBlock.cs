using Valour.Shared.Models;

namespace Valour.Server.Models;

public class UserBlock : ServerModel<long>, ISharedUserBlock
{
    public long UserId { get; set; }
    public long BlockedUserId { get; set; }
    public BlockType BlockType { get; set; }
    public DateTime CreatedAt { get; set; }
}
