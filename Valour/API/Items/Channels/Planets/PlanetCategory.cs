using Valour.Api.Client;
using Valour.Api.Items.Authorization;
using Valour.Api.Requests;
using Valour.Shared;
using Valour.Shared.Authorization;
using Valour.Shared.Items.Authorization;
using Valour.Api.Items.Planets.Members;
using Valour.Api.Nodes;
using Valour.Shared.Items.Channels.Planets;
using Valour.Api.Items.Planets;

namespace Valour.Api.Items.Channels.Planets;

/*  Valour - A free and secure chat client
 *  Copyright (C) 2021 Vooper Media LLC
 *  This program is subject to the GNU Affero General Public license
 *  A copy of the license should be included - if not, see <http://www.gnu.org/licenses/>
 */

public class PlanetCategory : PlanetChannel, ISharedPlanetCategory
{
    #region IPlanetItem implementation

    public override string BaseRoute =>
            $"api/{nameof(Planet)}/{PlanetId}/{nameof(PlanetCategory)}";

    #endregion

    /// <summary>
    /// True if this category inherits permissions from its parent
    /// </summary>
    public bool InheritsPerms { get; set; }

    public PermissionsTargetType PermissionsTargetType => PermissionsTargetType.PlanetCategoryChannel;

    public override string GetHumanReadableName() => "Category";

    /// <summary>
    /// Returns the item for the given id
    /// </summary>
    public static async Task<PlanetCategory> FindAsync(long id, long planetId, bool refresh = false)
    {
        if (!refresh)
        {
            var cached = ValourCache.Get<PlanetCategory>(id);
            if (cached is not null)
                return cached;
        }

        var node = await NodeManager.GetNodeForPlanetAsync(planetId);
        var item = (await node.GetJsonAsync<PlanetCategory>($"api/{nameof(Planet)}/{planetId}/{nameof(PlanetCategory)}/{id}")).Data;

        if (item is not null)
            await item.AddToCache();

        return item;
    }

    /// <summary>
    /// Returns the permissions node for the given role id
    /// </summary>
    public override async Task<PermissionsNode> GetPermissionsNodeAsync(long roleId, bool force_refresh = false) =>
        await GetCategoryPermissionsNodeAsync(roleId, force_refresh);


    /// <summary>
    /// Returns the category permissions node for the given role id
    /// </summary>
    public async Task<PermissionsNode> GetCategoryPermissionsNodeAsync(long roleId, bool force_refresh = false) =>
        await PermissionsNode.FindAsync(Id, roleId, PermissionsTargetType.PlanetCategoryChannel, force_refresh);

    /// <summary>
    /// Returns the category's default channel permissions node for the given role id
    /// </summary>
    public async Task<PermissionsNode> GetChannelPermissionsNodeAsync(long roleId, bool force_refresh = false) =>
        await PermissionsNode.FindAsync(Id, roleId, PermissionsTargetType.PlanetChatChannel, force_refresh);

    public async Task<TaskResult> SetChildOrderAsync(List<long> childIds) =>
        await Node.PostAsync($"{IdRoute}/children/order", childIds);

    public static async Task<TaskResult<PlanetCategory>> CreateWithDetails(CreatePlanetCategoryChannelRequest request)
    {
        var node = await NodeManager.GetNodeForPlanetAsync(request.Category.PlanetId);
        return await node.PostAsyncWithResponse<PlanetCategory>($"{request.Category.BaseRoute}/detailed", request);
    }

    /// <summary>
    /// Returns if the member has the given permission in this category
    /// </summary>
    public override async Task<bool> HasPermissionAsync(PlanetMember member, Permission permission)
    {
        Planet planet = await member.GetPlanetAsync();

        if (planet.OwnerId == member.UserId)
        {
            return true;
        }

        // If true, we ask the parent
        if (InheritsPerms)
        {
            return await (await GetParentAsync()).HasPermissionAsync(member, permission);
        }

        var roles = await member.GetRolesAsync();

        var doChannel = permission is ChatChannelPermission;

        // Starting from the most important role, we stop once we hit the first clear "TRUE/FALSE".
        // If we get an undecided, we continue to the next role down
        foreach (var role in roles.OrderBy(x => x.Position))
        {
            var node = await GetPermissionsNodeAsync(role.Id);

            // If we are dealing with the default role and the behavior is undefined, we fall back to the default permissions
            if (node == null)
            {
                if (role.Id == planet.DefaultRoleId)
                {
                    if (doChannel)
                        return Permission.HasPermission(ChatChannelPermissions.Default, permission);
                    else
                        return Permission.HasPermission(CategoryPermissions.Default, permission);
                }

                continue;
            }
            
            // If there is no view permission, there can't be any other permissions
            if (node.GetPermissionState(CategoryPermissions.View) == PermissionState.False)
                return false;

            var state = node.GetPermissionState(permission);

            switch (state)
            {
                case PermissionState.Undefined:
                    continue;
                case PermissionState.True:
                    return true;
                case PermissionState.False:
                default:
                    return false;
            }
        }

        // No roles ever defined behavior: resort to false.
        return false;
    }

    // Categories can't really be opened...
    public override Task Open()
        => Task.CompletedTask;

    public override Task Close()
        => Task.CompletedTask;
}

