using Valour.Shared.Models;
using Valour.Shared.Models;

namespace Valour.Server.Models;

public class PlanetVoiceChannel : Valour.Database.PlanetChannel, ISharedPlanetVoiceChannel
{
    public override PermissionsTargetType PermissionsTargetType
        => PermissionsTargetType.PlanetVoiceChannel;
}
