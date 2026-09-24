using Microsoft.AspNetCore.Mvc;
using Valour.Shared.Authorization;
using Valour.Shared.Models;

namespace Valour.Server.Api.Dynamic;

public class PermissionsNodeApi
{
    [ValourRoute(HttpVerbs.Get, "api/permissionsnodes/{id}")]
    [UserRequired(UserPermissionsEnum.Membership)]
    public static async Task<IResult> GetNodeRouteAsync(
        long id,
        PermissionsNodeService permissionsNodeService,
        PlanetMemberService memberService)
    {
        var node = await permissionsNodeService.GetAsync(id);
        if (node is null)
            return ValourResult.NotFound<PermissionsNode>();

        var member = await memberService.GetCurrentAsync(node.PlanetId);
        if (member is null)
            return ValourResult.NotPlanetMember();

        return Results.Json(node);
    }
    
    // Returns ALL permissions nodes for a planet
    [ValourRoute(HttpVerbs.Get, "api/permissionsnodes/all/{planetId}")]
    [UserRequired(UserPermissionsEnum.Membership)]
    public static async Task<IResult> GetAllForPlanetAsync(
        long planetId,
        PermissionsNodeService permissionsNodeService,
        PlanetMemberService memberService)
    {
        var member = await memberService.GetCurrentAsync(planetId);
        if (member is null)
            return ValourResult.NotPlanetMember();

        var nodes = await permissionsNodeService.GetAllAsync(planetId);
        if (nodes is null)
            return ValourResult.NotFound<PermissionsNode>();

        return Results.Json(nodes);
    }

    [ValourRoute(HttpVerbs.Get, "api/permissionsnodes/{type}/{targetId}/{roleId}")]
    [UserRequired(UserPermissionsEnum.Membership)]
    public static async Task<IResult> GetNodeForTargetRouteAsync(
        ChannelTypeEnum type,
        long targetId,
        long roleId,
        PermissionsNodeService permissionsNodeService,
        PlanetMemberService memberService)
    {
        var node = await permissionsNodeService.GetAsync(targetId, roleId, type);
        if (node is null)
            return ValourResult.NotFound<PermissionsNode>();

        var member = await memberService.GetCurrentAsync(node.PlanetId);
        if (member is null)
            return ValourResult.NotPlanetMember();

        return Results.Json(node);
    }

    [ValourRoute(HttpVerbs.Put, "api/permissionsnodes/{type}/{targetId}/{roleId}")]
    [UserRequired(UserPermissionsEnum.PlanetManagement)]
    // Planet permissions are not required in attribute because
    // There will be more permissions than just planet permissions!
    public static async Task<IResult> PutRouteAsync(
        [FromBody] PermissionsNode node,
        ChannelTypeEnum type,
        long targetId,
        long roleId,
        PermissionsNodeService permissionsNodeService,
        PlanetMemberService memberService,
        PlanetRoleService roleService,
        ChannelService channelService,
        PlanetPermissionService permissionService)
    {
        if (node is null)
            return Results.BadRequest("Include node in body.");

        // The route identifies the node. Only Code and Mask are taken from the body;
        // every identity field is replaced by the stored, authorized values below.
        var oldNode = await permissionsNodeService.GetAsync(targetId, roleId, type);
        if (oldNode is null)
            return ValourResult.NotFound<PermissionsNode>();

        var member = await memberService.GetCurrentAsync(oldNode.PlanetId);
        if (member is null)
            return ValourResult.NotPlanetMember();

        if (!await memberService.HasPermissionAsync(member, PlanetPermissions.ManageRoles))
            return ValourResult.LacksPermission(PlanetPermissions.ManageRoles);

        var role = await roleService.GetAsync(oldNode.PlanetId, oldNode.RoleId);
        if (role is null)
            return ValourResult.NotFound<PlanetRole>();

        if (await memberService.GetAuthorityAsync(member) <= role.GetAuthority())
            return ValourResult.Forbid("You can only modify permissions for roles below your own.");

        var target = await channelService.GetChannelAsync(oldNode.PlanetId, oldNode.TargetId);
        if (target is null)
            return ValourResult.NotFound<Channel>();

        var grantError = await GetUnheldNodeChangeErrorAsync(
            member, target, oldNode.TargetType, oldNode.Code, oldNode.Mask, node.Code, node.Mask, permissionService);
        if (grantError is not null)
            return ValourResult.Forbid(grantError);

        node.Id = oldNode.Id;
        node.PlanetId = oldNode.PlanetId;
        node.RoleId = oldNode.RoleId;
        node.TargetId = oldNode.TargetId;
        node.TargetType = oldNode.TargetType;

        var result = await permissionsNodeService.PutAsync(node);
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
        PlanetService planetService,
        PlanetMemberService memberService,
        PlanetRoleService roleService,
        ChannelService channelService,
        PlanetPermissionService permissionService)
    {
        if (node is null)
            return Results.BadRequest("Include node in body.");

        // Unfortunately we have to do the permissions in here
        var planet = await planetService.GetAsync(node.PlanetId);
        if (planet is null)
            return ValourResult.NotFound<Planet>();

        // The role and target are resolved inside the caller's planet, so a node
        // can never reference another planet's role or channel.
        var member = await memberService.GetCurrentAsync(planet.Id);
        if (member is null)
            return ValourResult.NotPlanetMember();

        if (!await memberService.HasPermissionAsync(member, PlanetPermissions.ManageRoles))
            return ValourResult.LacksPermission(PlanetPermissions.ManageRoles);

        var role = await roleService.GetAsync(planet.Id, node.RoleId);
        if (role is null)
            return ValourResult.NotFound<PlanetRole>();

        var target = await channelService.GetChannelAsync(planet.Id, node.TargetId);
        if (target is null)
            return ValourResult.NotFound<Channel>();

        if (target.ChannelType != node.TargetType)
        {
            if (target.ChannelType == ChannelTypeEnum.PlanetCategory)
            {
                if ((int)node.TargetType < 0 || (int)node.TargetType > (ChannelPermissions.ChannelTypes.Length - 1))
                {
                    return Results.BadRequest($"TargetType unknown ({node.TargetType}).");
                }
            }
            else
            {
                return Results.BadRequest("TargetType mismatch.");
            }
        }

        if (role.GetAuthority() >= await memberService.GetAuthorityAsync(member))
            return ValourResult.Forbid("The target node's role has higher authority than you.");

        var grantError = await GetUnheldNodeChangeErrorAsync(
            member, target, node.TargetType, 0, 0, node.Code, node.Mask, permissionService);
        if (grantError is not null)
            return ValourResult.Forbid(grantError);

        if (await permissionsNodeService.GetAsync(target.Id, role.Id, node.TargetType) is not null)
            return Results.BadRequest("A node already exists for this role and target.");

        node.PlanetId = planet.Id;
        node.RoleId = role.Id;
        node.TargetId = target.Id;

        var result = await permissionsNodeService.CreateAsync(node);
        if (!result.Success)
            return ValourResult.Problem(result.Message);

        return Results.Created($"api/permissionsnodes/{result.Data.Id}", result.Data);
    }

    /// <summary>
    /// Members may only allow or deny channel permission bits they hold in the target channel.
    /// Owners and admins hold every bit, so they are never restricted.
    /// </summary>
    private static async Task<string> GetUnheldNodeChangeErrorAsync(
        PlanetMember member,
        Channel target,
        ChannelTypeEnum targetType,
        long oldCode,
        long oldMask,
        long newCode,
        long newMask,
        PlanetPermissionService permissionService)
    {
        var changed = PermissionGrantGuard.GetChangedNodeBits(oldCode, oldMask, newCode, newMask);
        if (changed == 0)
            return null;

        var held = await permissionService.GetChannelPermissionsAsync(member, target, targetType);
        return PermissionGrantGuard.GetUnheldChangeError(
            changed, held, ChannelPermissions.GetChannelPermissionSet(targetType), "channel");
    }
}
