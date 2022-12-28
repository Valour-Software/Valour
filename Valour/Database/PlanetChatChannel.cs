using System.ComponentModel.DataAnnotations.Schema;
using Valour.Shared.Items.Authorization;
using Valour.Shared.Items.Channels.Planets;

namespace Valour.Database;

[Table("planet_chat_channels")]
public class PlanetChatChannel : PlanetChannel, ISharedPlanetChatChannel
{
    [Column("message_count")]
    public long MessageCount { get; set; }
    
    public override PermissionsTargetType PermissionsTargetType => PermissionsTargetType.PlanetChatChannel;
}

