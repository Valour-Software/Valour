using Valour.Shared.Models;
using Valour.Shared.Models;

namespace Valour.Server.Models;

public class PlanetChatChannel : PlanetChannel, ISharedPlanetChatChannel
{
    public override PermChannelType PermType => PermChannelType.PlanetChatChannel;
    
    /// <summary>
    /// True if this is the default chat channel
    /// </summary>
    [Column("is_default")]
    public bool IsDefault { get; set; }
}