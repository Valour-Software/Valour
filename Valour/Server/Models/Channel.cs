using Valour.Shared.Models;

namespace Valour.Server.Models;

public class Channel : Item, ISharedChannel
{
    /////////////////////////////////
    // Shared between all channels //
    /////////////////////////////////
    
    /// <summary>
    /// The name of the channel
    /// </summary>
    public string Name { get; set; }
    
    /// <summary>
    /// The description of the channel
    /// </summary>
    public string Description { get; set; }
    
    /// <summary>
    /// The type of this channel
    /// </summary>
    public ChannelTypeEnum ChannelType { get; set; }
    
    /// <summary>
    /// The last time a message was sent (or event occured) in this channel
    /// </summary>
    public DateTime LastUpdateTime { get; set; }
    
    /////////////////////////////
    // Only on planet channels //
    /////////////////////////////
    
    /// <summary>
    /// The id of the planet this channel belongs to, if any
    /// </summary>
    public long? PlanetId { get; set; }
    
    /// <summary>
    /// The id of the parent of the channel, if any
    /// </summary>
    public long? ParentId { get; set; }
    
    /// <summary>
    /// The position of the channel in the channel list
    /// </summary>
    public int? Position { get; set; }
    
    /// <summary>
    /// If this channel inherits permissions from its parent
    /// </summary>
    public bool? InheritsPerms { get; set; }
    
    /// <summary>
    /// If this channel is the default channel
    /// </summary>
    public bool? IsDefault { get; set; }
}
