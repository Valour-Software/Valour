using Microsoft.AspNetCore.Mvc;
using Valour.Shared.Authorization;

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
        PlanetService planetService)
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

        if (role.GetAuthority() > await memberService.GetAuthorityAsync(member))
            return ValourResult.Forbid("You cannot create roles with higher authority than your own.");

        if (role.IsAdmin && !await memberService.IsAdminAsync(member.Id))
            return ValourResult.Forbid("Only an admin can create admin roles");

        var result = await roleService.CreateAsync(role);
        if (!result.Success)
            return ValourResult.Problem(result.Message);

        return Results.Created($"api/roles/{result.Data.Id}", result.Data);
    }

    [ValourRoute(HttpVerbs.Put, "api/planet/{planetId}/roles/{id}")]
    [UserRequired(UserPermissionsEnum.PlanetManagement)]
    public static async Task<IResult> PutRouteAsync(
        [FromBody] PlanetRole role,
        long planetId,
        long roleId,
        PlanetMemberService memberService,
        PlanetRoleService roleService)
    {
        var oldRole = await roleService.GetAsync(planetId, roleId);
        if (oldRole is null)
            return ValourResult.NotFound("Role not found.");

        // Get member
        var member = await memberService.GetCurrentAsync(oldRole.PlanetId);
        if (member is null)
            return ValourResult.NotPlanetMember();

        if (!await memberService.HasPermissionAsync(member, PlanetPermissions.ManageRoles))
            return ValourResult.LacksPermission(PlanetPermissions.ManageRoles);

        if (await memberService.GetAuthorityAsync(member) <= role.GetAuthority())
            return ValourResult.Forbid("You can only edit roles under your own.");
        
        if (oldRole.IsDefault != role.IsDefault)
            return ValourResult.BadRequest("You cannot change if a role is default.");
        
        if ((oldRole.IsAdmin != role.IsAdmin) && !await memberService.IsAdminAsync(member.Id))
            return ValourResult.Forbid("Only an admin can change admin state of roles");

        var result = await roleService.UpdateAsync(role);
        if (!result.Success)
            return ValourResult.Problem(result.Message);

        return Results.Json(result.Data);
    }

    [ValourRoute(HttpVerbs.Delete, "api/planet/{planetId}/roles/{id}")]
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
}