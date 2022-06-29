using Valour.Api.Items.Authorization;
using Valour.Shared.Items.Authorization;
using Valour.Shared.Items.Planets.Channels;

namespace Valour.Api.Items.Planets.Channels;

/*  Valour - A free and secure chat client
 *  Copyright (C) 2021 Vooper Media LLC
 *  This program is subject to the GNU Affero General Public license
 *  A copy of the license should be included - if not, see <http://www.gnu.org/licenses/>
 */

public class PlanetCategoryChannel : SyncedItem<PlanetCategoryChannel>, ISharedPlanetCategoryChannel, IPlanetChannel
{
    /// <summary>
    /// The Id of the planet this category belongs to
    /// </summary>
    public ulong PlanetId { get; set; }

    /// <summary>
    /// The name of this category
    /// </summary>
    public string Name { get; set; }

    /// <summary>
    /// The position of this category (lower is higher)
    /// </summary>
    public int Position { get; set; }

    /// <summary>
    /// The description of this category
    /// </summary>
    public string Description { get; set; }

    /// <summary>
    /// The Id of the parent category this category belongs to
    /// </summary>
    public ulong? ParentId { get; set; }

    /// <summary>
    /// True if this category inherits permissions from its parent
    /// </summary>
    public bool InheritsPerms { get; set; }

    public string GetHumanReadableName() => "Category";

    /// <summary>
    /// Returns the planet of this category
    /// </summary>

    public async Task<Planet> GetPlanetAsync(bool refresh = false) =>
        await Planet.FindAsync(PlanetId, refresh);

    /// <summary>
    /// Returns the permissions node for the given role id
    /// </summary>
    public async Task<PermissionsNode> GetPermissionsNodeAsync(ulong roleId, bool force_refresh = false) =>
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

