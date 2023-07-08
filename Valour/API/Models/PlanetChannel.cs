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
public class PlanetChannel : Channel, IPlanetModel, ISharedPlanetChannel, IOrderedModel
{
    #region IPlanetModel implementation

    public long PlanetId { get; set; }

    public ValueTask<Planet> GetPlanetAsync(bool refresh = false) =>
        IPlanetModel.GetPlanetAsync(this, refresh);

    public override string BaseRoute =>
            $"api/channels";

    #endregion

    // Cached values
    protected List<PermissionsNode> PermissionsNodes { get; set; }

    public int Position { get; set; }
    public long? ParentId { get; set; }
    public bool InheritsPerms { get; set; }
    public virtual ChannelType Type => ChannelType.Undefined;

    public virtual string GetHumanReadableName() => "UNKNOWN TYPE";

    public override Task Open()
    {
        throw new NotImplementedException();
    }

    public override Task Close()
    {
        throw new NotImplementedException();
    }

    public virtual async ValueTask<PlanetChannel> GetParentAsync()
    {
        if (ParentId is null)
        {
            return null;
        }
        return await PlanetCategory.FindAsync(ParentId.Value, PlanetId);
    }

    public static PlanetChannel GetCachedByType(long id, ChannelType type)
    {
        switch (type)
        {
            default:
                throw new NotImplementedException("Unknown channel type");
            case ChannelType.PlanetChatChannel:
                return ValourCache.Get<PlanetChatChannel>(id);
            case ChannelType.PlanetCategoryChannel:
                return ValourCache.Get<PlanetCategory>(id);
            case ChannelType.PlanetVoiceChannel:
                return ValourCache.Get<PlanetVoiceChannel>(id);
        }
    }

    /// <summary>
    /// Requests and caches nodes from the server
    /// </summary>
    public virtual async Task LoadPermissionNodesAsync(bool refresh = false)
    {
        var planet = await GetPlanetAsync();
        var allPermissions = await planet.GetPermissionsNodesAsync(refresh);
        
        if (PermissionsNodes is not null)
            PermissionsNodes.Clear();
        else
            PermissionsNodes = new List<PermissionsNode>();
        
        foreach (var node in allPermissions)
        {
            if (node.TargetId == Id)
                PermissionsNodes.Add(node);
        }
    }

    public virtual async Task<PermissionsNode> GetPermNodeAsync(long roleId, ChannelType? type = null, bool refresh = false)
    {
        if (type is null)
            type = Type;

        if (PermissionsNodes is null || refresh)
            await LoadPermissionNodesAsync();

        return PermissionsNodes.FirstOrDefault(x => x.RoleId == roleId && x.TargetType == type);
    }

    public virtual async Task<bool> HasPermissionAsync(PlanetMember member, Permission permission)
    {
        var planet = await member.GetPlanetAsync();

        // Owners have all permissions
        if (planet.OwnerId == member.UserId)
            return true;
        
        var memberRoles = await member.GetRolesAsync();

        var target = this;

        // Move up until no longer inheriting
        while (target.InheritsPerms && target.ParentId is not null)
            target = await target.GetParentAsync();

        var viewPerm = PermissionState.Undefined;

        foreach (var role in memberRoles)
        {
            var node = await GetPermNodeAsync(role.Id, permission.TargetType);
            if (node is null)
                continue;

            viewPerm = node.GetPermissionState(ChatChannelPermissions.View, true);
            if (viewPerm != PermissionState.Undefined)
                break;
        }

        if (viewPerm == PermissionState.Undefined)
        {
            var _topRole = memberRoles.FirstOrDefault() ?? PlanetRole.DefaultRole;
            viewPerm = Permission.HasPermission(_topRole.ChatPermissions, ChatChannelPermissions.View) ? PermissionState.True : PermissionState.False;
        }

        if (viewPerm != PermissionState.True)
            return false;

        // Go through roles in order
        foreach (var role in memberRoles)
        {
            var node = await GetPermNodeAsync(role.Id, permission.TargetType);
            if (node is null)
                continue;

            // (A lot of the logic here is identical to the server-side PlanetMemberService.HasPermissionAsync)

            // If there is no view permission, there can't be any other permissions
            // View is always 0x01 for channel permissions, so it is safe to use ChatChannelPermission.View for
            // all cases.

            var state = node.GetPermissionState(permission, true);

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

        var topRole = memberRoles.FirstOrDefault() ?? PlanetRole.DefaultRole;

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

