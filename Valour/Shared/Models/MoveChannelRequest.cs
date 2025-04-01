namespace Valour.Shared.Models;

public class MoveChannelRequest
{
    /// <summary>
    /// The planet the move is taking place in
    /// </summary>
    public long PlanetId { get; set; }
    
    /// <summary>
    /// The channel which is being moved
    /// </summary>
    public long MovingChannel { get; set; }
    
    /// <summary>
    /// The destination of the moving channel
    /// </summary>
    public long? DestinationChannel { get; set; }
    
    /// <summary>
    /// True if the channel should be inserted before the destination channel, false if it should be inserted after
    /// </summary>
    public bool PlaceBefore { get; set; }
}