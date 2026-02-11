using Valour.Shared.Models;

namespace Valour.Shared.Authorization;

/// <summary>
/// Shared pure-static permission calculator used by both server and client.
/// </summary>
public static class PermissionCalculator
{
    /// <summary>
    /// OR's together planet permissions from all roles.
    /// Short-circuits to FULL_CONTROL if any role is admin.
    /// </summary>
    public static long GetPlanetPermissions<TRole>(IEnumerable<TRole> roles)
        where TRole : ISharedPlanetRole
    {
        long permissions = 0;
        foreach (var role in roles)
        {
            if (role.IsAdmin)
                return Permission.FULL_CONTROL;

            permissions |= role.Permissions;
        }

        return permissions;
    }

    /// <summary>
    /// Returns the channel-type-specific base permissions for a single role.
    /// </summary>
    public static long GetRoleChannelPermissions(ISharedPlanetRole role, ChannelTypeEnum channelType)
    {
        return channelType switch
        {
            ChannelTypeEnum.PlanetChat => role.ChatPermissions,
            ChannelTypeEnum.PlanetCategory => role.CategoryPermissions,
            ChannelTypeEnum.PlanetVoice => role.VoicePermissions,
            _ => 0
        };
    }

    /// <summary>
    /// OR's together channel-type-specific base permissions from all roles.
    /// Short-circuits to FULL_CONTROL if any role is admin.
    /// </summary>
    public static long GetBaseChannelPermissions<TRole>(IEnumerable<TRole> roles, ChannelTypeEnum channelType)
        where TRole : ISharedPlanetRole
    {
        long permissions = 0;
        foreach (var role in roles)
        {
            if (role.IsAdmin)
                return Permission.FULL_CONTROL;

            permissions |= GetRoleChannelPermissions(role, channelType);
        }

        return permissions;
    }

    /// <summary>
    /// Applies permission nodes to base permissions.
    /// Nodes must be ordered weakest-role-first (highest Position first) so that
    /// the strongest role's node has the final say (last-write-wins).
    /// </summary>
    public static long ApplyPermissionNodes<TNode>(long basePermissions, IEnumerable<TNode> nodesWeakestFirst)
        where TNode : ISharedPermissionsNode
    {
        long permissions = basePermissions;
        foreach (var node in nodesWeakestFirst)
        {
            permissions &= ~node.Mask;
            permissions |= (node.Code & node.Mask);
        }

        return permissions;
    }

    /// <summary>
    /// Computes final channel permissions by OR'ing all roles' base permissions
    /// then applying permission nodes (weakest-first).
    /// </summary>
    public static long GetChannelPermissions<TRole, TNode>(
        IEnumerable<TRole> roles,
        ChannelTypeEnum channelType,
        IEnumerable<TNode> nodesWeakestFirst)
        where TRole : ISharedPlanetRole
        where TNode : ISharedPermissionsNode
    {
        var basePermissions = GetBaseChannelPermissions(roles, channelType);

        // Admin roles yield FULL_CONTROL from GetBaseChannelPermissions;
        // skip node application so deny nodes can't reduce admin permissions.
        if (basePermissions == Permission.FULL_CONTROL)
            return Permission.FULL_CONTROL;

        return ApplyPermissionNodes(basePermissions, nodesWeakestFirst);
    }
}
