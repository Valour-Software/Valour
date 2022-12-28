using Valour.Shared;
using Valour.Shared.Items.Authorization;
using Valour.Shared.Items.Channels.Planets;

namespace Valour.Server.Models;

public class PlanetCategory : PlanetChannel, ISharedPlanetCategory
{
    public override PermissionsTargetType PermissionsTargetType => PermissionsTargetType.PlanetCategoryChannel;
}