namespace Valour.Shared.Models;

public interface ISharedBlockedUser
{
    /// <summary>
    ///  The id of the user who initiated the block
    /// </summary>
    long SourceUserId { get; set; }

    /// <summary>
    /// The user who is being blocked
    /// </summary>
    long TargetUserId { get; set; }

    string Reason { get; set; }
    
    DateTime Timestamp { get; set; }
}