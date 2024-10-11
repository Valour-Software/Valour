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
    int Position { get; set; }
    
    /// <summary>
    /// The depth, or how many categories deep the channel is
    /// </summary>
    int Depth { get; }
    
    /// <summary>
    /// The position of the channel within its parent
    /// </summary>
    int LocalPosition { get; }
    
    public static int GetDepth(ISharedChannel channel) => GetDepth(channel.Position);

    public static int GetDepth(int position)
    {
        // Check if the third and fourth bytes (depth 3 and 4) are present
        if ((position & 0x0000FFFF) == 0)
        {
            // If they are not, we must be in the first or second layer
            if ((position & 0x00FF0000) == 0)
            {
                // If the second byte is also zero, it's in the first layer (top level)
                return 0;
            }
            // Otherwise, it's in the second layer
            return 1;
        }
        else
        {
            // Check the lowest byte first (fourth layer)
            if ((position & 0x000000FF) == 0)
            {
                // If the fourth byte is zero, it's in the third layer
                return 2;
            }
            
            // If none of the previous checks matched, it’s in the fourth layer
            return 3;
        }   
    }

    public static int GetLocalPosition(ISharedChannel channel) => GetLocalPosition(channel.Position);
    
    public static int GetLocalPosition(int position)
    {
        var depth = GetDepth(position);
        // use depth to determine amount to shift
        var shift = 8 * (3 - depth);
        var shifted = position >> shift;
        // now clear the higher bits
        return shifted & 0xFF;
    }
    
    public static int AppendRelativePosition(int parentPosition, int relativePosition)
    {
        var depth = GetDepth(parentPosition) + 1;
        // use depth to determine amount to shift
        var shift = 8 * (3 - depth);
        // shift the relative position to the correct position
        var shifted = relativePosition << shift;
        // now add the relative position to the parent position
        return parentPosition | shifted;
    }

    int ISortableModel.GetSortPosition()
    {
        return Position;
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
