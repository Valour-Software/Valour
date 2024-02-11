using System.ComponentModel.DataAnnotations.Schema;
using Valour.Shared.Channels;

namespace Valour.Sdk.Models;

public class UserChannelState : ISharedUserChannelState
{
    public long ChannelId { get; set; }
    public long UserId { get; set; }
    public DateTime LastViewedTime { get; set; }
}
