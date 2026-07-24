using Valour.Shared.Models;

namespace Valour.Shared.Channels;
public interface ISharedUserChannelState
{
    long ChannelId { get; set; }
    long UserId { get; set; }
    DateTime LastViewedTime { get; set; }
    ChannelActivityAlerts ActivityAlerts { get; set; }
}
