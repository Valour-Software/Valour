using IdGen;
using Microsoft.AspNetCore.Mvc;
using Valour.Shared.Authorization;
using Valour.Shared.Models;

namespace Valour.Server.Api.Dynamic;

public class PermissionsNodeApi
{
    [ValourRoute(HttpVerbs.Get, "api/permissionsnodes/{id}")]
    public static async Task<IResult> GetNodeRouteAsync(
        long id, 
        PermissionsNodeService permissionsNodeService)
    {
        var node = await permissionsNodeService.GetAsync(id);
        if (node is null)
            return ValourResult.NotFound<PermissionsNode>();

        return Results.Json(node);
    }

    [ValourRoute(HttpVerbs.Get, "api/permissionsnodes/{type}/{targetId}/{roleId}")]
    public static async Task<IResult> GetNodeForTargetRouteAsync(
        PermissionsTargetType type, 
        long targetId, 
        long roleId,
        PermissionsNodeService permissionsNodeService)
    {
        var node = await permissionsNodeService.GetAsync(targetId, roleId, type);
        if (node is null)
            return ValourResult.NotFound<PermissionsNode>();

        return Results.Json(node);
    }

    [ValourRoute(HttpVerbs.Put, "api/permissionsnodes/{type}/{targetId}/{roleId}")]
    [UserRequired(UserPermissionsEnum.PlanetManagement)]
    // Planet permissions are not required in attribute because
    // There will be more permissions than just planet permissions!
    public static async Task<IResult> PutRouteAsync(
        [FromBody] PermissionsNode node,
        PermissionsTargetType type,
        long targetId, 
        long roleId,
        PermissionsNodeService permissionsNodeService,
        PlanetMemberService memberService,
        PlanetService planetService,
        PlanetRoleService roleService)
    {
        if (node.TargetId != targetId)
            return Results.BadRequest("TargetId mismatch");
        if (node.RoleId != roleId)
            return Results.BadRequest("RoleId mismatch");
        if (node.TargetType != type)
            return Results.BadRequest("Type mismatch");

        // Unfortunately we have to do the permissions in here
        var planet = await planetService.GetAsync(node.PlanetId);
        if (planet is null)
            return ValourResult.NotFound<Planet>();

        var member = await memberService.GetCurrentAsync(planet.Id);
        if (member is null)
            return ValourResult.NotPlanetMember();

        if (!await memberService.HasPermissionAsync(member, PlanetPermissions.ManageRoles))
            return ValourResult.LacksPermission(PlanetPermissions.ManageRoles);

        var oldNode = await permissionsNodeService.GetAsync(targetId, roleId, type);
        if (oldNode is null)
            return ValourResult.NotFound<PermissionsNode>();

        if (oldNode.RoleId != node.RoleId)
            return Results.BadRequest("Cannot change RoleId");

        if (oldNode.TargetId != node.TargetId)
            return Results.BadRequest("Cannot change TargetId");

        if (oldNode.TargetType != node.TargetType)
            return Results.BadRequest("Cannot change TargetType");

        var role = await roleService.GetAsync(node.RoleId);
        if (role is null)
            return ValourResult.NotFound<PlanetRole>();

        if (await memberService.GetAuthorityAsync(member) <= role.GetAuthority())
            return ValourResult.Forbid("You can only modify permissions for roles below your own.");

        var result = await permissionsNodeService.PutAsync(oldNode, node);
        if (!result.Success)
            return ValourResult.Problem(result.Message);

        return Results.Json(result.Data);
    }

    [ValourRoute(HttpVerbs.Post, $"api/permissionsnodes")]
    [UserRequired(UserPermissionsEnum.PlanetManagement)]
    // Planet permissions are not required in attribute because
    // There will be more permissions than just planet permissions!
    public static async Task<IResult> PostRouteAsync(
        [FromBody] PermissionsNode node,
        PermissionsNodeService permissionsNodeService,
        UserService userService,
        PlanetService planetService,
        PlanetMemberService memberService,
        PlanetRoleService roleService,
        PlanetChannelService channelService)
    {
        var userId = await userService.GetCurrentUserId();

        // Unfortunately we have to do the permissions in here
        var planet = await planetService.GetAsync(node.PlanetId);
        if (planet is null)
            return ValourResult.NotFound<Planet>();

        var member = await memberService.GetByUserAsync(userId, planet.Id);
        if (member is null)
            return ValourResult.NotPlanetMember();

        if (!await memberService.HasPermissionAsync(member, PlanetPermissions.ManageRoles))
            return ValourResult.LacksPermission(PlanetPermissions.ManageRoles);

        var role = await roleService.GetAsync(node.RoleId);
        if (role is null)
            return ValourResult.NotFound<PlanetRole>();

        var target = await channelService.GetAsync(node.TargetId);
        if (target is null)
            return ValourResult.NotFound<PlanetChannel>();

        if (target.PermissionsTargetType != node.TargetType)
        {
            // Special case: Categories have a sub-node with PlanetChatChannel type!
            if (!(target.PermissionsTargetType == PermissionsTargetType.PlanetCategoryChannel &&
                node.TargetType == PermissionsTargetType.PlanetChatChannel))
            {
                return Results.BadRequest("TargetType mismatch.");
            }
        }  

        if (role.GetAuthority() > await memberService.GetAuthorityAsync(member))
            return ValourResult.Forbid("The target node's role has higher authority than you.");

        if (await permissionsNodeService.GetAsync(node.TargetId, node.RoleId, node.TargetType) is not null)
            return Results.BadRequest("A node already exists for this role and target.");

        var result = await permissionsNodeService.CreateAsync(node);
        if (!result.Success)
            return ValourResult.Problem(result.Message);

        return Results.Created($"api/permissionsnodes/{result.Data.Id}", result.Data);
    }
}