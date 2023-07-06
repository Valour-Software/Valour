using Valour.Shared.Models;

namespace Valour.Server.Models;

public class PlanetVoiceChannel : PlanetChannel, ISharedPlanetVoiceChannel
{
    public override ChannelType Type
        => ChannelType.PlanetVoiceChannel;
}
