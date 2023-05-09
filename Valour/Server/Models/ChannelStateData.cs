namespace Valour.Server.Models;

public class ChannelStateData
{
    /// <summary>
    /// Id of the channel
    /// </summary>
    public long ChannelId { get; set; }

    /// <summary>
    /// The state the channel is reporting
    /// </summary>
    public ChannelState ChannelState { get; set; }
    
    /// <summary>
    /// The most current state viewed by the user
    /// </summary>
    public DateTime LastViewedTime { get; set; }
}