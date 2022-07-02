using Valour.Api.Items.Authorization;
using Valour.Shared.Items.Authorization;
using Valour.Shared.Items.Planets.Channels;

namespace Valour.Api.Items.Planets.Channels;

/*  Valour - A free and secure chat client
 *  Copyright (C) 2021 Vooper Media LLC
 *  This program is subject to the GNU Affero General Public license
 *  A copy of the license should be included - if not, see <http://www.gnu.org/licenses/>
 */

public class PlanetCategoryChannel : PlanetChannel<PlanetCategoryChannel>, ISharedPlanetCategoryChannel
{
    /// <summary>
    /// True if this category inherits permissions from its parent
    /// </summary>
    public bool InheritsPerms { get; set; }

    public override string GetHumanReadableName() => "Category";

    /// <summary>
    /// Returns the planet of this category
    /// </summary>

    public override async Task<Planet> GetPlanetAsync(bool refresh = false) =>
        await Planet.FindAsync(PlanetId, refresh);

    /// <summary>
    /// Returns the permissions node for the given role id
    /// </summary>
    public override async Task<PermissionsNode> GetPermissionsNodeAsync(ulong roleId, bool force_refresh = false) =>
        await GetCategoryPermissionsNodeAsync(roleId, force_refresh);


    /// <summary>
    /// Returns the category permissions node for the given role id
    /// </summary>
    public  async Task<PermissionsNode> GetCategoryPermissionsNodeAsync(ulong roleId, bool force_refresh = false) =>
        await PermissionsNode.FindAsync(Id, roleId, PermissionsTarget.PlanetCategoryChannel, force_refresh);

    /// <summary>
    /// Returns the category's default channel permissions node for the given role id
    /// </summary>
    public async Task<PermissionsNode> GetChannelPermissionsNodeAsync(ulong roleId, bool force_refresh = false) =>
        await PermissionsNode.FindAsync(Id, roleId, PermissionsTarget.PlanetChatChannel, force_refresh);


}

