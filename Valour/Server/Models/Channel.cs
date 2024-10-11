using Valour.Shared.Models;

namespace Valour.Server.Models;

public class Channel : ServerModel, ISharedChannel
{
    /////////////////////////////////
    // Shared between all channels //
    /////////////////////////////////
    
    public List<ChannelMember> Members { get; set; }
    
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
    /// The position of the channel. Works as the following:
    /// [8 bits]-[8 bits]-[8 bits]-[8 bits]
    /// Each 8 bits is a category, with the first category being the top level
    /// So for example, if a channel is in the 3rd category of the 2nd category of the 1st category,
    /// [00000011]-[00000010]-[00000001]-[00000000]
    /// This does limit the depth of categories to 4, and the highest position
    /// to 254 (since 000 means no position)
    /// </summary>
    public int Position { get; set; }
    
    /// <summary>
    /// The depth, or how many categories deep the channel is
    /// </summary>
    public int Depth => ISharedChannel.GetDepth(this);

    /// <summary>
    /// The position of the channel within its parent
    /// </summary>
    public int LocalPosition => ISharedChannel.GetLocalPosition(this);
    
    /// <summary>
    /// If this channel inherits permissions from its parent
    /// </summary>
    public bool InheritsPerms { get; set; }
    
    /// <summary>
    /// If this channel is the default channel
    /// </summary>
    public bool IsDefault { get; set; }
}
