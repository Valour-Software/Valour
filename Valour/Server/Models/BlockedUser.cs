using Valour.Shared.Models;

namespace Valour.Server.Models;

public class BlockedUser : ISharedBlockedUser
{
    /// <summary>
    ///  The id of the user who initiated the block
    /// </summary>
    public long SourceUserId { get; set; }

    /// <summary>
    /// The user who is being blocked
    /// </summary>
    public long TargetUserId { get; set; }

    public string Reason { get; set; }
    
    public DateTime Timestamp { get; set; }
}