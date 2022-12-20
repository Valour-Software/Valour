using Valour.Api.Items.Planets;
using Valour.Server.Database;
using Valour.Server.Database.Items.Channels.Planets;
using Valour.Server.Database.Items.Planets.Members;
using Valour.Shared.Authorization;
using Valour.Shared.Items.Authorization;

namespace Valour.Server.Services;

public class PermissionsService
{
    private readonly ValourDB _db;
    private readonly PlanetService _planetService;
    private readonly PlanetMemberService _memberService;
    private readonly PlanetCategoryService _categoryService;
    
    public PermissionsService(
        ValourDB db, 
        PlanetService planetService,
        PlanetMemberService memberService,
        PlanetCategoryService categoryService)
    {
        _db = db;
        _planetService = planetService;
        _memberService = memberService;
        _categoryService = categoryService;
    }

    public async Task<bool> HasPermissionAsync(PlanetMember member, PlanetPermission permission)
    {
        if (member is null)
            return false;
        
        // Special case for viewing planets
        // All existing members can view a planet
        if (permission.Value == PlanetPermissions.View.Value)
        {
            return true;
        }
        
        var planet = await member.GetPlanetAsync(_planetService);

        // Owner has all permissions
        if (member.UserId == planet.OwnerId)
            return true;

        // Get user main role
        var mainRole = await member.GetPrimaryRoleAsync(_memberService);

        // Return permission state
        return mainRole.HasPermission(permission);
    }
    
    public async Task<bool> HasPermissionAsync(PlanetMember member, PlanetChannel channel, Permission permission)
    {
        var planet = await channel.GetPlanetAsync(_planetService);

        if (planet.OwnerId == member.UserId)
            return true;

        // If the channel inherits from its parent, move up until it does not
        while (channel.InheritsPerms)
        {
            channel = await channel.GetParentAsync(_categoryService);
        }
        
        // Load permission data
        // This loads the roles and the node for the specific channel
        var roles = await member.GetRolesAndNodesAsync(channel.Id, permission.TargetType, _memberService);
        
        // Starting from the most important role, we stop once we hit the first clear "TRUE/FALSE".
        // If we get an undecided, we continue to the next role down
        foreach (var role in roles)
        {
            // If the role has a node for this channel, we use that
            var node = role.PermissionNodes.FirstOrDefault();
            
            // We continue to the next role
            // if the node is null
            if (node is null)
                continue;

            // If there is no view permission, there can't be any other permissions
            // View is always 0x01 for chanel permissions, so it is safe to use ChatChannelPermission.View for
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

        // Fallback to default permissions
        return Permission.HasPermission(permission.GetDefault(), permission);
    }
}