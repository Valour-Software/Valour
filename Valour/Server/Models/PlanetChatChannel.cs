using Valour.Shared.Items.Authorization;
using Valour.Shared.Items.Channels.Planets;

namespace Valour.Server.Models;

public class PlanetChatChannel : PlanetChannel, ISharedPlanetChannel
{
    public long MessageCount { get; set; }
    
    public override PermissionsTargetType PermissionsTargetType => PermissionsTargetType.PlanetChatChannel;
}