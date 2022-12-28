using System.ComponentModel.DataAnnotations.Schema;
using Valour.Shared.Items.Authorization;
using Valour.Shared.Items.Channels.Planets;

namespace Valour.Database;

[Table("planet_category_channels")]
public class PlanetCategory : PlanetChannel, ISharedPlanetCategory
{
    public override PermissionsTargetType PermissionsTargetType => PermissionsTargetType.PlanetCategoryChannel;
}

