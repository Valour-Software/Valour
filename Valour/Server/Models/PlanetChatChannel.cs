using Valour.Shared.Models;
using Valour.Shared.Models;

namespace Valour.Server.Models;

public class PlanetChatChannel : PlanetChannel, ISharedPlanetChannel
{
    public long MessageCount { get; set; }
    
    public override PermissionsTargetType PermissionsTargetType => PermissionsTargetType.PlanetChatChannel;
}