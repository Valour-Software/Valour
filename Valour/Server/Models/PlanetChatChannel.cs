using Valour.Shared.Models;
using Valour.Shared.Models;

namespace Valour.Server.Models;

public class PlanetChatChannel : PlanetChannel, ISharedPlanetChannel
{
    public override PermChannelType PermType => PermChannelType.PlanetChatChannel;
}