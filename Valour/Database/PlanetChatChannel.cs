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
    
    [Column("message_count")]
    public long MessageCount { get; set; }
    
    public override PermissionsTargetType PermissionsTargetType => PermissionsTargetType.PlanetChatChannel;
}

