using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Valour.Api.Client;
using Valour.Api.Nodes;
using Valour.Api.Requests;
using Valour.Shared;
using Valour.Shared.Authorization;
using Valour.Shared.Models;
using Valour.Shared.Models;

namespace Valour.Api.Models;

public class PlanetVoiceChannel : PlanetChannel, IVoiceChannel, ISharedPlanetVoiceChannel
{
    #region IPlanetItem implementation

    public override string BaseRoute =>
            $"api/planetvoicechannels";

    #endregion

    /// <summary>
    /// True if this channel inherits permissions from its parent
    /// </summary>
    public bool InheritsPerms { get; set; }

    /// <summary>
    /// Returns the name of the item type
    /// </summary>
    public override string GetHumanReadableName() => "Voice Channel";

    public PermissionsTargetType PermissionsTargetType => PermissionsTargetType.PlanetVoiceChannel;

    public override async Task Open() =>
        await Task.CompletedTask;

    public override async Task Close() =>
        await Task.CompletedTask;

    /// <summary>
    /// Returns the item for the given id
    /// </summary>
    public static async ValueTask<PlanetVoiceChannel> FindAsync(long id, long planetId, bool refresh = false)
    {
        if (!refresh)
        {
            var cached = ValourCache.Get<PlanetVoiceChannel>(id);
            if (cached is not null)
                return cached;
        }

        var node = await NodeManager.GetNodeForPlanetAsync(planetId);
        var item = (await node.GetJsonAsync<PlanetVoiceChannel>($"api/planetvoicechannels/{id}")).Data;

        if (item is not null)
            await item.AddToCache();

        return item;
    }

    /// <summary>
    /// Returns the permissions node for the given role id
    /// </summary>
    public override async Task<PermissionsNode> GetPermissionsNodeAsync(long roleId, bool refresh = false) =>
        await GetChannelPermissionsNodeAsync(roleId, refresh);

    /// <summary>
    /// Returns the voice channel permissions node for the given role id
    /// </summary>
    public async Task<PermissionsNode> GetChannelPermissionsNodeAsync(long roleId, bool refresh = false) =>
        await PermissionsNode.FindAsync(Id, roleId, PermissionsTargetType.PlanetVoiceChannel, refresh);

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
            TargetType = PermissionsTargetType.PlanetVoiceChannel
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

            foreach (var perm in VoiceChannelPermissions.Permissions)
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
    /// Returns if the member has the given permission in this voice channel
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
                    return Permission.HasPermission(VoiceChannelPermissions.Default, permission);
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

    public static async Task<TaskResult<PlanetVoiceChannel>> CreateWithDetails(CreatePlanetVoiceChannelRequest request)
    {
        var node = await NodeManager.GetNodeForPlanetAsync(request.Channel.PlanetId);
        return await node.PostAsyncWithResponse<PlanetVoiceChannel>($"{request.Channel.BaseRoute}/detailed", request);
    }

    public async Task<bool> HasPermissionAsync(long memberId, VoiceChannelPermission perm) =>
        (await Node.GetJsonAsync<bool>($"{IdRoute}/checkperm/{memberId}/{perm.Value}")).Data;
}
