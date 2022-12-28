using Valour.Shared.Items.Channels.Users;

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

    /// <summary>
    /// The number of messages in the channel
    /// </summary>
    public long MessageCount { get; set; }
}