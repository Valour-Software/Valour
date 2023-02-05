using Valour.Shared;
using Valour.Shared.Models;
using Valour.Shared.Models;

namespace Valour.Server.Models;

public class PlanetCategory : PlanetChannel, ISharedPlanetCategory
{
    public override PermChannelType PermType => PermChannelType.PlanetCategoryChannel;
}