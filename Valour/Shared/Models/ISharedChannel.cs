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
    
    PlanetVideo = 7,
}

public static class SharedChannelNames
{
    public static readonly string[] ChannelTypeNames = new string[]
    {
        "Planet Chat Channel",
        "Planet Category",
        "Planet Voice Channel",
        "Direct Chat Channel",
        "Direct Voice Channel",
        "Group Chat Channel",
        "Group Voice Channel",
        "Planet Video Channel"
    };
}

public interface ISharedChannel : ISharedModel<long>, ISortable
{
    public const byte CurrentVersion = 2;
    public const string DirectBaseRoute = "api/channels/direct";
    public static string GetBaseRoute(ISharedChannel channel) => channel.PlanetId.HasValue ? GetPlanetBaseRoute(channel.PlanetId.Value) : DirectBaseRoute;
    public static string GetIdRoute(ISharedChannel channel) => channel.PlanetId.HasValue ? GetPlanetIdRoute(channel.PlanetId.Value, channel.Id) : GetDirectIdRoute(channel.Id);
    public static string GetDirectIdRoute(long id) => $"{DirectBaseRoute}/{id}";
    public static string GetPlanetBaseRoute(long planetId) => $"{ISharedPlanet.BaseRoute}/{planetId}/channels";
    public static string GetPlanetIdRoute(long planetId, long id) => $"{GetPlanetBaseRoute(planetId)}/{id}";
    
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
        ChannelTypeEnum.PlanetVoice,
        ChannelTypeEnum.PlanetVideo
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
        ChannelTypeEnum.PlanetVideo,
        ChannelTypeEnum.DirectVoice,
        ChannelTypeEnum.GroupVoice
    };

    public static readonly HashSet<ChannelTypeEnum> CallChannelTypes = new()
    {
        ChannelTypeEnum.PlanetVoice,
        ChannelTypeEnum.PlanetVideo,
        ChannelTypeEnum.DirectVoice,
        ChannelTypeEnum.GroupVoice
    };

    public static bool IsPlanetCallType(ChannelTypeEnum type) =>
        type is ChannelTypeEnum.PlanetVoice or ChannelTypeEnum.PlanetVideo;

    public static ChannelTypeEnum GetPermissionTargetType(ChannelTypeEnum type) =>
        type == ChannelTypeEnum.PlanetVideo ? ChannelTypeEnum.PlanetVoice : type;
    
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
    /// The full position of the channel. Includes full hierarchy.
    /// </summary>
    uint RawPosition { get; set; }
    
    uint ISortable.GetSortPosition()
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

    /// <summary>
    /// If this channel is marked as NSFW
    /// </summary>
    bool Nsfw { get; set; }
    
    /// <summary>
    /// For call channels, the associated chat channel id used for integrated chat.
    /// </summary>
    long? AssociatedChatChannelId { get; set; }
}
