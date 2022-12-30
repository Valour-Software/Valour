using Valour.Shared.Models;

namespace Valour.Server.Models;

public class PlanetVoiceChannel : PlanetChannel, ISharedPlanetVoiceChannel
{
    public override PermissionsTargetType PermissionsTargetType
        => PermissionsTargetType.PlanetVoiceChannel;
}
