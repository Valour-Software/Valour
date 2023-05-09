namespace Valour.Shared.Models;

public interface ISharedChannelState
{
    /// <summary>
    /// The id of the channel this state is for
    /// </summary>
    long ChannelId { get; set; }
    
    /// <summary>
    /// The id of the planet this state's channel belongs to, if it is in a planet
    /// </summary>
    long? PlanetId { get; set; }
    
    /// <summary>
    /// The last time at which the channel had a state change which should mark it as
    /// unread to clients
    /// </summary>
    DateTime LastUpdateTime { get; set; }
}