using Valour.Shared.Items.Authorization;
using Valour.Shared.Items.Channels.Planets;

namespace Valour.Server.Models;

public class PlanetVoiceChannel : Valour.Database.PlanetChannel, ISharedPlanetVoiceChannel
{
    public override PermissionsTargetType PermissionsTargetType
        => PermissionsTargetType.PlanetVoiceChannel;
}
