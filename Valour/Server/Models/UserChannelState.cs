using Valour.Shared.Channels;

namespace Valour.Server.Models;

public class UserChannelState : ISharedUserChannelState
{
    public long ChannelId { get; set; }
    public long UserId { get; set; }
    public long? PlanetId { get; set; }
    public long? PlanetMemberId { get; set; } // Null if not a planet channel
    public DateTime LastViewedTime { get; set; }
}
