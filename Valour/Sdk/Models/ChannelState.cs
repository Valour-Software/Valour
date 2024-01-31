using Valour.Shared.Models;

namespace Valour.Sdk.Models;

/// <summary>
/// Represents the state of a channel, used to determine
/// read states and other functions
/// </summary>
public class ChannelState : ISharedChannelState
{
    /// <summary>
    /// The id of the channel this state is for
    /// </summary>
    public long ChannelId { get; set; }
    
    /// <summary>
    /// The id of the planet this state's channel belongs to, if it is in a planet
    /// </summary>
    public long? PlanetId { get; set; }
    
    /// <summary>
    /// The last time at which the channel had a state change which should mark it as
    /// unread to clients
    /// </summary>
    public DateTime LastUpdateTime { get; set; }
}