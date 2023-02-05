using Valour.Shared.Channels;

namespace Valour.Server.Models;

public class UserChannelState : ISharedUserChannelState
{
    public long ChannelId { get; set; }
    
    public long UserId { get; set; }
    
    public DateTime LastViewedTime { get; set; }
}
