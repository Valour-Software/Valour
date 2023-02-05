using System.Runtime.InteropServices;
using System.Text.Json.Serialization;
using Valour.Api.Client;
using Valour.Shared;
using Valour.Shared.Authorization;
using Valour.Shared.Models;

namespace Valour.Api.Models;

[JsonDerivedType(typeof(PlanetChatChannel), typeDiscriminator: nameof(PlanetChatChannel))]
[JsonDerivedType(typeof(PlanetVoiceChannel), typeDiscriminator: nameof(PlanetVoiceChannel))]
[JsonDerivedType(typeof(PlanetCategory), typeDiscriminator: nameof(PlanetCategory))]
public abstract class PlanetChannel : Channel, IPlanetItem, ISharedPlanetChannel
{
    #region IPlanetItem implementation

    public long PlanetId { get; set; }

    public ValueTask<Planet> GetPlanetAsync(bool refresh = false) =>
        IPlanetItem.GetPlanetAsync(this, refresh);

    public override string BaseRoute =>
            $"api/channels";

    #endregion

    // Cached values
    protected List<PermissionsNode> PermissionsNodes { get; set; }

    public int Position { get; set; }
    public long? ParentId { get; set; }
    public bool InheritsPerms { get; set; }
    public abstract PermChannelType PermType { get; }

    public abstract string GetHumanReadableName();

    public async ValueTask<PlanetChannel> GetParentAsync()
    {
        if (ParentId is null)
        {
            return null;
        }
        return await PlanetCategory.FindAsync(ParentId.Value, PlanetId);
    }

    /// <summary>
    /// Requests and caches nodes from the server
    /// </summary>
    public async Task LoadPermissionNodesAsync()
    {
        var nodes = (await Node.GetJsonAsync<List<PermissionsNode>>($"{IdRoute}/nodes")).Data;
        if (nodes is null)
            return;

        // Update cache values
        foreach (var node in nodes)
        {
            // Skip event for bulk loading
            await ValourCache.Put(node.Id, node, true);
        }

        // Create container if needed
        if (PermissionsNodes == null)
            PermissionsNodes = new List<PermissionsNode>();
        else
            PermissionsNodes.Clear();

        // Retrieve cache values (this is necessary to ensure single copies of items)
        foreach (var node in nodes)
        {
            var cNode = ValourCache.Get<PermissionsNode>(node.Id);

            if (cNode is not null)
                PermissionsNodes.Add(cNode);
        }
    }

    public async Task<PermissionsNode> GetPermNodeAsync(long roleId, PermChannelType? type = null, bool force_refresh = false)
    {
        if (type is null)
            type = PermType;

        if (PermissionsNodes is null || force_refresh)
            await LoadPermissionNodesAsync();

        return PermissionsNodes.FirstOrDefault(x => x.RoleId == roleId && x.TargetType == type);
    }

    public async Task<bool> HasPermissionAsync(PlanetMember member, Permission permission)
    {
        var planet = await member.GetPlanetAsync();

        // Owners have all permissions
        if (planet.OwnerId == member.UserId)
            return true;
        
        var roles = await member.GetRolesAsync();

        var target = this;

        // Move up until no longer inheriting
        while (target.InheritsPerms && target.ParentId is not null)
            target = await target.GetParentAsync();

        // Go through roles in order
        foreach (var role in roles)
        {
            var node = await GetPermNodeAsync(role.Id, permission.TargetType);
            if (node is null)
                continue;

            // (A lot of the logic here is identical to the server-side PlanetMemberService.HasPermissionAsync)

            // If there is no view permission, there can't be any other permissions
            // View is always 0x01 for channel permissions, so it is safe to use ChatChannelPermission.View for
            // all cases.
            if (node.GetPermissionState(ChatChannelPermissions.View) == PermissionState.False)
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

        var topRole = roles.FirstOrDefault() ?? PlanetRole.DefaultRole;

        // Fallback to base permissions
        switch (permission)
        {
            case ChatChannelPermission:
                return Permission.HasPermission(topRole.ChatPermissions, permission);
            case CategoryPermission:
                return Permission.HasPermission(topRole.CategoryPermissions, permission);
            case VoiceChannelPermission:
                return Permission.HasPermission(topRole.VoicePermissions, permission);
            default:
                throw new Exception("Unexpected permission type: " + permission.GetType().Name);
        }
    }
}

