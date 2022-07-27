using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json.Serialization;
using Valour.Api.Client;
using Valour.Api.Items.Authorization;
using Valour.Api.Items.Messages;
using Valour.Api.Items.Planets.Members;
using Valour.Api.Requests;
using Valour.Shared;
using Valour.Shared.Authorization;
using Valour.Shared.Items;
using Valour.Shared.Items.Authorization;
using Valour.Shared.Items.Planets.Channels;

namespace Valour.Api.Items.Planets.Channels;


/*  Valour - A free and secure chat client
*  Copyright (C) 2021 Vooper Media LLC
*  This program is subject to the GNU Affero General Public license
*  A copy of the license should be included - if not, see <http://www.gnu.org/licenses/>
*/

public class PlanetChatChannel : PlanetChannel, ISharedPlanetChatChannel
{
    #region IPlanetItem implementation

    public override string BaseRoute =>
            $"/api/{nameof(Planet)}/{PlanetId}/{nameof(PlanetChatChannel)}";

    #endregion

    /// <summary>
    /// The total number of messages sent in this channel
    /// </summary>
    public long MessageCount { get; set; }

    /// <summary>
    /// True if this channel inherits permissions from its parent
    /// </summary>
    public bool InheritsPerms { get; set; }

    /// <summary>
    /// Returns the name of the item type
    /// </summary>
    public override string GetHumanReadableName() => "Chat Channel";

    public PermissionsTargetType PermissionsTargetType => PermissionsTargetType.PlanetChatChannel;

    /// <summary>
    /// Returns the permissions node for the given role id
    /// </summary>
    public override async Task<PermissionsNode> GetPermissionsNodeAsync(long roleId, bool refresh = false) =>
        await GetChannelPermissionsNodeAsync(roleId, refresh);

    /// <summary>
    /// Returns the channel permissions node for the given role id
    /// </summary>
    public async Task<PermissionsNode> GetChannelPermissionsNodeAsync(long roleId, bool refresh = false) =>
        await PermissionsNode.FindAsync(Id, roleId, PermissionsTargetType.PlanetChatChannel, refresh);

    /// <summary>
    /// Returns the current total permissions for this channel for a member.
    /// This result is NOT SYNCED, since it flattens several nodes into one!
    /// </summary>
    public async ValueTask<PermissionsNode> GetMemberPermissionsAsync(long memberId, long planetId, bool force_refresh = false)
    {
        var member = await PlanetMember.FindAsync(memberId, planetId);
        var roles = await member.GetRolesAsync();

        // Start with no permissions
        var dummy_node = new PermissionsNode()
        {
            // Full, since values should either be yes or no
            Mask = Permission.FULL_CONTROL,
            // Default to no permission
            Code = 0x0,

            PlanetId = PlanetId,
            TargetId = Id,
            TargetType = PermissionsTargetType.PlanetChatChannel
        };

        var planet = await GetPlanetAsync();

        // Easy cheat for owner
        if (planet.OwnerId == member.UserId)
        {
            dummy_node.Code = Permission.FULL_CONTROL;
            return dummy_node;
        }

        // Should be in order of most power -> least,
        // so we reverse it here
        for (int i = roles.Count - 1; i >= 0; i--)
        {
            var role = roles[i];
            var node = await GetChannelPermissionsNodeAsync(role.Id, force_refresh);
            if (node is null)
            {
                continue;
            }

            foreach (var perm in ChatChannelPermissions.Permissions)
            {
                var val = node.GetPermissionState(perm);

                // Change nothing if undefined. Otherwise overwrite.
                // Since most important nodes come last, we will end with correct perms.
                if (val == PermissionState.True)
                {
                    dummy_node.SetPermission(perm, PermissionState.True);
                }
                else if (val == PermissionState.False)
                {
                    dummy_node.SetPermission(perm, PermissionState.False);
                }
            }
        }

        return dummy_node;
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

        // Starting from the most important role, we stop once we hit the first clear "TRUE/FALSE".
        // If we get an undecided, we continue to the next role down
        foreach (var role in roles.OrderBy(x => x.Position))
        {
            PermissionsNode node = null;

            node = await GetPermissionsNodeAsync(role.Id);

            // If we are dealing with the default role and the behavior is undefined, we fall back to the default permissions
            if (node == null)
            {
                if (role.Id == planet.DefaultRoleId)
                {
                    return Permission.HasPermission(ChatChannelPermissions.Default, permission);
                }
                continue;
            }

            PermissionState state = PermissionState.Undefined;

            state = node.GetPermissionState(permission);

            if (state == PermissionState.Undefined)
            {
                continue;
            }
            else if (state == PermissionState.True)
            {
                return true;
            }
            else
            {
                return false;
            }
        }

        // No roles ever defined behavior: resort to false.
        return false;
    }

    /// <summary>
    /// Returns the item for the given id
    /// </summary>
    public static async ValueTask<PlanetChatChannel> FindAsync(long id, long planetId, bool refresh = false)
    {
        if (!refresh)
        {
            var cached = ValourCache.Get<PlanetChatChannel>(id);
            if (cached is not null)
                return cached;
        }

        var item = (await ValourClient.GetJsonAsync<PlanetChatChannel>($"api/{nameof(Planet)}/{planetId}/{nameof(PlanetChatChannel)}/{id}")).Data;

        if (item is not null)
            await item.AddToCache();

        return item;
    }

    public static async Task<TaskResult<PlanetChatChannel>> CreateWithDetails(CreatePlanetChatChannelRequest request)
    {
        return await ValourClient.PostAsyncWithResponse<PlanetChatChannel>($"{request.Channel.BaseRoute}/detailed", request);
    } 

    public async Task<bool> HasPermissionAsync(long memberId, ChatChannelPermission perm) =>
        (await ValourClient.GetJsonAsync<bool>($"{IdRoute}/checkperm/{memberId}/{perm.Value}")).Data;

    /// <summary>
    /// Returns the last (count) messages starting at (index)
    /// </summary>
    public async Task<List<PlanetMessage>> GetMessagesAsync(long index = long.MaxValue, int count = 10) =>
        (await ValourClient.GetJsonAsync<List<PlanetMessage>>($"{IdRoute}/messages?index={index}&count={count}")).Data;

    /// <summary>
    /// Returns the last (count) messages
    /// </summary>
    public async Task<List<PlanetMessage>> GetLastMessagesAsync(int count = 10) =>
        (await ValourClient.GetJsonAsync<List<PlanetMessage>>($"{IdRoute}/messages?count={count}")).Data;
}

