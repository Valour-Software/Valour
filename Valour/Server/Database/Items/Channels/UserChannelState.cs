using Valour.Shared.Channels;

namespace Valour.Server.Database.Items.Channels;

[Table("user_channel_states")]
public class UserChannelState : ISharedUserChannelState
{
    [Column("channel_id")]
    public long ChannelId { get; set; }

    [Column("user_id")]
    public long UserId { get; set; }

    [Column("last_viewed_state")]
    public string LastViewedState { get; set; }
}
