using Valour.Shared.Channels;
using Valour.Shared.Models;

namespace Valour.Sdk.Models;

public class UserChannelState : ISharedUserChannelState
{
    public long ChannelId { get; set; }
    public long UserId { get; set; }
    public DateTime LastViewedTime { get; set; }
    public ChannelActivityAlerts ActivityAlerts { get; set; }
}
