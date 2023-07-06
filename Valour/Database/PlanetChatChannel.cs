using System.ComponentModel.DataAnnotations.Schema;
using Valour.Shared.Models;
using Valour.Shared.Models;

namespace Valour.Database;

[Table("planet_chat_channels")]
public class PlanetChatChannel : PlanetChannel, ISharedPlanetChatChannel
{
    ///////////////////////
    // Entity Properties //
    ///////////////////////

    public override ChannelType Type => ChannelType.PlanetChatChannel;
    
    /// <summary>
    /// True if this is the default chat channel
    /// </summary>
    [Column("is_default")]
    public bool IsDefault { get; set; }
}

