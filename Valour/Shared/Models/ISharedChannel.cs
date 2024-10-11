using System.ComponentModel.DataAnnotations.Schema;
using System.Runtime.CompilerServices;

namespace Valour.Shared.Models;

public enum ChannelTypeEnum
{
    Undefined = -1,
    
    PlanetChat = 0,
    PlanetCategory = 1,
    PlanetVoice = 2,
    
    DirectChat = 3,
    DirectVoice = 4,
    
    GroupChat = 5,
    GroupVoice = 6,
}

public class SharedChannelNames
{
    public static readonly string[] ChannelTypeNames = new string[]
    {
        "Planet Chat Channel",
        "Planet Category",
        "Planet Voice Channel",
        "Direct Chat Channel",
        "Direct Voice Channel",
        "Group Chat Channel",
        "Group Voice Channel"
    };
}

public interface ISharedChannel : ISharedModel, ISortableModel
{
    public static string GetTypeName(ChannelTypeEnum type)
    {
        var i = (int)type;
        if (i < 0 || i >= SharedChannelNames.ChannelTypeNames.Length)
            return "UNDEFINED";
        
        return SharedChannelNames.ChannelTypeNames[(int)type];
    }
    
    public static readonly HashSet<ChannelTypeEnum> PlanetChannelTypes = new ()
    {
        ChannelTypeEnum.PlanetChat,
        ChannelTypeEnum.PlanetCategory,
        ChannelTypeEnum.PlanetVoice
    };
    
    public static readonly HashSet<ChannelTypeEnum> ChatChannelTypes = new ()
    {
        ChannelTypeEnum.PlanetChat,
        ChannelTypeEnum.DirectChat,
        ChannelTypeEnum.GroupChat
    };
    
    public static readonly HashSet<ChannelTypeEnum> VoiceChannelTypes = new ()
    {
        ChannelTypeEnum.PlanetVoice,
        ChannelTypeEnum.DirectVoice,
        ChannelTypeEnum.GroupVoice
    };
    
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
    
    /// <summary>
    /// The position of the channel. Works as the following:
    /// [8 bits]-[8 bits]-[8 bits]-[8 bits]
    /// Each 8 bits is a category, with the first category being the top level
    /// So for example, if a channel is in the 3rd category of the 2nd category of the 1st category,
    /// [00000011]-[00000010]-[00000001]-[00000000]
    /// This does limit the depth of categories to 4, and the highest position
    /// to 254 (since 000 means no position)
    /// </summary>
    public uint RawPosition { get; set; }
    
    uint ISortableModel.GetSortPosition()
    {
        return RawPosition;
    }

    /////////////////////////////////////
    // Only applies to planet channels //
    /////////////////////////////////////
    
    /// <summary>
    /// The id of the planet this channel belongs to, if any
    /// </summary>
    long? PlanetId { get; set; }
    
    /// <summary>
    /// The id of the parent of the channel, if any
    /// </summary>
    long? ParentId { get; set; }
    
    /// <summary>
    /// If this channel inherits permissions from its parent
    /// </summary>
    bool InheritsPerms { get; set; }
    
    /// <summary>
    /// If this channel is the default channel
    /// </summary>
    bool IsDefault { get; set; }
}
