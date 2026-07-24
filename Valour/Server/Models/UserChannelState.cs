using Valour.Shared.Channels;
using Valour.Shared.Models;

namespace Valour.Server.Models;

public class UserChannelState : ISharedUserChannelState
{
    public long ChannelId { get; set; }
    public long UserId { get; set; }
    public long? PlanetId { get; set; }
    public long? PlanetMemberId { get; set; } // Null if not a planet channel
    public DateTime LastViewedTime { get; set; }
    public ChannelActivityAlerts ActivityAlerts { get; set; }
}
