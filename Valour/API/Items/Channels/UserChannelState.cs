using System.ComponentModel.DataAnnotations.Schema;
using Valour.Shared.Channels;

namespace Valour.Api.Items.Channels;

public class UserChannelState : ISharedUserChannelState
{
    public long ChannelId { get; set; }
    public long UserId { get; set; }
    public string LastViewedState { get; set; }
}
