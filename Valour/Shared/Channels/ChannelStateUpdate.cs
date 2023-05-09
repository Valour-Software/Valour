namespace Valour.Shared.Models;

public struct ChannelStateUpdate
{
    public long ChannelId { get; set; }
    public long? PlanetId { get; set; }
    public DateTime Time { get; set; }

    public ChannelStateUpdate(long channelId, DateTime time, long? planetId = null)
    {
        this.ChannelId = channelId;
        this.Time = time;
        this.PlanetId = planetId;
    }
}
