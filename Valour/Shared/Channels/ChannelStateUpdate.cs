namespace Valour.Shared.Models;

public struct ChannelStateUpdate
{
    public long ChannelId { get; set; }
    public DateTime Time { get; set; }

    public ChannelStateUpdate(long channelId, DateTime time)
    {
        this.ChannelId = channelId;
        this.Time = time;
    }
}
