using System.ComponentModel.DataAnnotations.Schema;
using Valour.Shared.Models;
using Valour.Shared.Models;

namespace Valour.Database;

[Table("planet_voice_channels")]
public class PlanetVoiceChannel : PlanetChannel, ISharedPlanetVoiceChannel
{
    ///////////////////////
    // Entity Properties //
    ///////////////////////
    
    public override ChannelType Type
        => ChannelType.PlanetVoiceChannel;
}
