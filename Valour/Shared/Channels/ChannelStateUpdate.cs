namespace Valour.Shared.Items.Channels;

public struct ChannelStateUpdate
{
    public long ChannelId { get; set; }
    public string State { get; set; }

    public ChannelStateUpdate(long channelId, string state)
    {
        this.ChannelId = channelId;
        this.State = state;
    }
}
