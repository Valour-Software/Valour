namespace Valour.Shared.Models;

public enum ChannelTypeEnum
{
    PlanetChat,
    DirectChat,
    GroupChat,
    PlanetCategory,
    PlanetVoice,
    DirectVoice,
    GroupVoice,
}

public interface ISharedChannel : ISharedItem
{
    /////////////////////////////////
    // Shared between all channels //
    /////////////////////////////////
    
    /// <summary>
    /// The name of the channel
    /// </summary>
    string Name { get; set; }
    
    /// <summary>
    /// The description of the channel
    /// </summary>
    string Description { get; set; }
    
    /// <summary>
    /// The type of this channel
    /// </summary>
    ChannelTypeEnum ChannelType { get; set; }
    
    /// <summary>
    /// The last time a message was sent (or event occured) in this channel
    /// </summary>
    DateTime LastUpdateTime { get; set; }
    
    /////////////////////////////
    // Only on planet channels //
    /////////////////////////////
    
    /// <summary>
    /// The id of the planet this channel belongs to, if any
    /// </summary>
    long? PlanetId { get; set; }
    
    /// <summary>
    /// The id of the parent of the channel, if any
    /// </summary>
    long? ParentId { get; set; }
    
    /// <summary>
    /// The position of the channel in the channel list
    /// </summary>
    int? Position { get; set; }
    
    /// <summary>
    /// If this channel inherits permissions from its parent
    /// </summary>
    bool? InheritsPerms { get; set; }
    
    /// <summary>
    /// If this channel is the default channel
    /// </summary>
    bool? IsDefault { get; set; }
}
