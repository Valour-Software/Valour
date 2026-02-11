using Valour.Shared.Models;

namespace Valour.Server.Models;

public class FriendEventData
{
    /// <summary>
    /// The user the event is about
    /// </summary>
    public User User { get; set; }

    /// <summary>
    /// The type of friend event
    /// </summary>
    public FriendEventType Type { get; set; }
}
