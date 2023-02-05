using Valour.Shared.Models;

namespace Valour.Server.Models;

public class DirectChatChannel : Channel, ISharedDirectChatChannel
{
    /// <summary>
    /// The id of one of the users in the DM channel
    /// </summary>
    public long UserOneId { get; set; }

    /// <summary>
    /// The id of one of the users in the DM channel
    /// </summary>
    public long UserTwoId { get; set; }
}