namespace Valour.Shared.Models;

public class ChannelMove
{
    /// <summary>
    /// The ID of the channel that moved
    /// </summary>
    public long ChannelId { get; set; }
    
    /// <summary>
    /// The new full position of the channel
    /// </summary>
    public uint NewFullPosition { get; set; }
    
    /// <summary>
    /// The new local position of the channel
    /// </summary>
    public byte NewLocalPosition { get; set; }
    
    /// <summary>
    /// The new parent of the channel
    /// </summary>
    public long? NewParentId { get; set; }
}

public class ChannelsMovedEvent
{
    /// <summary>
    /// The ID of the planet the channels are being moved in
    /// </summary>
    public long PlanetId { get; set; }
    
    /// <summary>
    /// The moves that are taking place
    /// </summary>
    public ChannelMove[] Moves { get; set; }
    
    public ChannelsMovedEvent(long planetId)
    {
        PlanetId = planetId;
    }
}