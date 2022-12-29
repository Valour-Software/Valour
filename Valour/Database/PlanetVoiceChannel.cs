using System.ComponentModel.DataAnnotations.Schema;
using Valour.Shared.Items.Authorization;
using Valour.Shared.Items.Channels.Planets;

namespace Valour.Database;

[Table("planet_voice_channels")]
public class PlanetVoiceChannel : PlanetChannel, ISharedPlanetVoiceChannel
{
    public override PermissionsTargetType PermissionsTargetType
        => PermissionsTargetType.PlanetVoiceChannel;
}
