using Microsoft.AspNetCore.Mvc;
using Valour.Shared.Authorization;
using Valour.Shared.Models;

namespace Valour.Server.Api.Dynamic;

public class PlanetRoleApi
{
    [ValourRoute(HttpVerbs.Get, "api/planet/{planetId}/roles/{roleId}")]
    [UserRequired(UserPermissionsEnum.Membership)]
    public static async Task<IResult> GetRouteAsync(
        long planetId,
        long roleId, 
        PlanetRoleService roleService,
        PlanetMemberService memberService)
    {
        // Get the role
        var role = await roleService.GetAsync(planetId, roleId);
        if (role is null)
            return ValourResult.NotFound("Role not found");

        // Get member
        var member = await memberService.GetCurrentAsync(role.PlanetId);
        if (member is null)
            return ValourResult.NotPlanetMember();

        // Return json
        return Results.Json(role);
    }


    [ValourRoute(HttpVerbs.Post, "api/planet/{planetId}/roles")]
    [UserRequired(UserPermissionsEnum.PlanetManagement)]
    public static async Task<IResult> PostRouteAsync(
        long planetId,
        [FromBody] PlanetRole role,
        PlanetRoleService roleService,
        PlanetMemberService memberService,
        PlanetPermissionService permissionService)
    {
        if (role is null)
            return ValourResult.BadRequest("Include role in body.");
        
        if (role.PlanetId != planetId)
            return ValourResult.BadRequest("Role planet id does not match route planet id.");
        
        // Get member
        var member = await memberService.GetCurrentAsync(role.PlanetId);
        if (member is null)
            return ValourResult.NotPlanetMember();
        
        if (role.IsDefault)
            return ValourResult.BadRequest("You cannot create another default role.");

        if (!await memberService.HasPermissionAsync(member, PlanetPermissions.ManageRoles))
            return ValourResult.LacksPermission(PlanetPermissions.ManageRoles);
        
        if (role.GetAuthority() > await memberService.GetAuthorityAsync(member))
            return ValourResult.Forbid("You cannot create roles with higher authority than your own.");

        if (role.IsAdmin && !await memberService.IsAdminAsync(member.Id))
            return ValourResult.Forbid("Only an admin can create admin roles");

        var grantError = await GetUnheldPermissionErrorAsync(member, null, role, permissionService);
        if (grantError is not null)
            return ValourResult.Forbid(grantError);

        var result = await roleService.CreateAsync(role);
        if (!result.Success)
            return ValourResult.Problem(result.Message);

        return Results.Created(ISharedPlanetRole.GetIdRoute(planetId, result.Data.Id), result.Data);
    }

    [ValourRoute(HttpVerbs.Put, "api/planet/{planetId}/roles/{roleId}")]
    [UserRequired(UserPermissionsEnum.PlanetManagement)]
    public static async Task<IResult> PutRouteAsync(
        [FromBody] PlanetRole role,
        long planetId,
        long roleId,
        PlanetMemberService memberService,
        PlanetRoleService roleService,
        PlanetPermissionService permissionService)
    {
        if (role is null)
            return ValourResult.BadRequest("Include role in body.");

        if (role.Id != roleId)
            return ValourResult.BadRequest("Role id in body does not match route role id.");

        if (role.PlanetId != planetId)
            return ValourResult.BadRequest("Role planet id does not match route planet id.");

        var oldRole = await roleService.GetAsync(planetId, roleId);
        if (oldRole is null)
            return ValourResult.NotFound("Role not found.");

        // Get member
        var member = await memberService.GetCurrentAsync(oldRole.PlanetId);
        if (member is null)
            return ValourResult.NotPlanetMember();

        if (!await memberService.HasPermissionAsync(member, PlanetPermissions.ManageRoles))
            return ValourResult.LacksPermission(PlanetPermissions.ManageRoles);

        if (await memberService.GetAuthorityAsync(member) <= oldRole.GetAuthority())
            return ValourResult.Forbid("You can only edit roles under your own.");
        
        if (oldRole.IsDefault != role.IsDefault)
            return ValourResult.BadRequest("You cannot change if a role is default.");
        
        if ((oldRole.IsAdmin != role.IsAdmin) && !await memberService.IsAdminAsync(member.Id))
            return ValourResult.Forbid("Only an admin can change admin state of roles");

        if (oldRole.FlagBitIndex != role.FlagBitIndex)
            return ValourResult.BadRequest("You cannot change the role's flag bit index.");

        var grantError = await GetUnheldPermissionErrorAsync(member, oldRole, role, permissionService);
        if (grantError is not null)
            return ValourResult.Forbid(grantError);

        var result = await roleService.UpdateAsync(role);
        if (!result.Success)
            return ValourResult.Problem(result.Message);

        return Results.Json(result.Data);
    }

    [ValourRoute(HttpVerbs.Delete, "api/planet/{planetId}/roles/{roleId}")]
    [UserRequired(UserPermissionsEnum.PlanetManagement)]
    public static async Task<IResult> DeleteRouteAsync(
        long planetId,
        long roleId,
        PlanetMemberService memberService,
        PlanetRoleService roleService)
    {
        var role = await roleService.GetAsync(planetId, roleId);
        if (role is null)
            return ValourResult.NotFound("Role not found.");

        // Get member
        var member = await memberService.GetCurrentAsync(role.PlanetId);
        if (member is null)
            return ValourResult.NotPlanetMember();

        if (!await memberService.HasPermissionAsync(member, PlanetPermissions.ManageRoles))
            return ValourResult.LacksPermission(PlanetPermissions.ManageRoles);

        if (await memberService.GetAuthorityAsync(member) <= role.GetAuthority())
            return ValourResult.Forbid("You can only delete roles under your own.");
        
        if (role.IsAdmin && !await memberService.IsAdminAsync(member.Id))
            return ValourResult.Forbid("Only an admin can delete admin roles");
        
        if (role.IsDefault)
            return ValourResult.BadRequest("You cannot delete the default role.");

        var result = await roleService.DeleteAsync(role);
        if (!result.Success)
            return ValourResult.Problem(result.Message);

        return Results.NoContent();

    }

    [ValourRoute(HttpVerbs.Get, "api/planet/{planetId}/roles/{roleId}/nodes")]
    [UserRequired(UserPermissionsEnum.Membership)]
    public static async Task<IResult> GetNodesRouteAsync(
        long planetId,
        long roleId,
        PlanetMemberService memberService,
        PlanetRoleService roleService)
    {
        var role = await roleService.GetAsync(planetId, roleId);
        if (role is null)
            return ValourResult.NotFound("Role not found.");

        // Get member
        var member = await memberService.GetCurrentAsync(role.PlanetId);
        if (member is null)
            return ValourResult.NotPlanetMember();

        var nodes = await roleService.GetNodesAsync(roleId);

        return Results.Json(nodes);

    }

    /// <summary>
    /// Members may only grant or revoke role permission bits they hold themselves.
    /// Owners and admins hold every bit, so they are never restricted.
    /// </summary>
    private static async Task<string> GetUnheldPermissionErrorAsync(
        PlanetMember member,
        PlanetRole oldRole,
        PlanetRole newRole,
        PlanetPermissionService permissionService)
    {
        // View is implicitly granted to every member
        var heldPlanet = await permissionService.GetRolePermissionsAsync(member) | PlanetPermissions.View.Value;
        var error = PermissionGrantGuard.GetUnheldChangeError(
            (oldRole?.Permissions ?? 0) ^ newRole.Permissions, heldPlanet,
            PlanetPermissions.Permissions, "planet");
        if (error is not null)
            return error;

        error = PermissionGrantGuard.GetUnheldChangeError(
            (oldRole?.ChatPermissions ?? 0) ^ newRole.ChatPermissions,
            await permissionService.GetRolePermissionsAsync(member, ChannelTypeEnum.PlanetChat),
            ChatChannelPermissions.Permissions, "chat channel");
        if (error is not null)
            return error;

        error = PermissionGrantGuard.GetUnheldChangeError(
            (oldRole?.CategoryPermissions ?? 0) ^ newRole.CategoryPermissions,
            await permissionService.GetRolePermissionsAsync(member, ChannelTypeEnum.PlanetCategory),
            CategoryPermissions.Permissions, "category");
        if (error is not null)
            return error;

        return PermissionGrantGuard.GetUnheldChangeError(
            (oldRole?.VoicePermissions ?? 0) ^ newRole.VoicePermissions,
            await permissionService.GetRolePermissionsAsync(member, ChannelTypeEnum.PlanetVoice),
            VoiceChannelPermissions.Permissions, "voice channel");
    }
}
