using Microsoft.AspNetCore.Mvc;
using Valour.Shared.Authorization;

namespace Valour.Server.Api.Dynamic;

public class PlanetRoleApi
{
    [ValourRoute(HttpVerbs.Get, "api/planetroles/{id}")]
    [UserRequired(UserPermissionsEnum.Membership)]
    public static async Task<IResult> GetRouteAsync(
        long id, 
        PlanetRoleService roleService,
        PlanetMemberService memberService,
        PlanetChatChannelService channelService)
    {
        // Get the role
        var role = await roleService.GetAsync(id);
        if (role is null)
            return ValourResult.NotFound("Role not found");

        // Get member
        var member = await memberService.GetCurrentAsync(role.PlanetId);
        if (member is null)
            return ValourResult.NotPlanetMember();

        // Return json
        return Results.Json(role);
    }


    [ValourRoute(HttpVerbs.Post, "api/planetroles")]
    [UserRequired(UserPermissionsEnum.PlanetManagement)]
    public static async Task<IResult> PostRouteAsync(
        [FromBody] PlanetRole role,
        PlanetRoleService roleService,
        PlanetMemberService memberService)
    {
        // Get member
        var member = await memberService.GetCurrentAsync(role.PlanetId);
        if (member is null)
            return ValourResult.NotPlanetMember();

        if (role.GetAuthority() > await memberService.GetAuthorityAsync(member))
            return ValourResult.Forbid("You cannot create roles with higher authority than your own.");

        var result = await roleService.CreateAsync(role);
        if (!result.Success)
            return ValourResult.Problem(result.Message);

        return Results.Created($"api/planetroles/{role.Id}", role);
    }

    [ValourRoute(HttpVerbs.Put, "api/planetroles/{id}")]
    [UserRequired(UserPermissionsEnum.PlanetManagement)]
    public static async Task<IResult> PutRouteAsync(
        [FromBody] PlanetRole role, 
        long id,
        PlanetMemberService memberService,
        PlanetRoleService roleService)
    {
        var oldRole = await roleService.GetAsync(id);

        // Get member
        var member = await memberService.GetCurrentAsync(oldRole.PlanetId);
        if (member is null)
            return ValourResult.NotPlanetMember();

        if (!await memberService.HasPermissionAsync(member, PlanetPermissions.ManageRoles))
            return ValourResult.LacksPermission(PlanetPermissions.ManageRoles);

        var result = await roleService.PutAsync(oldRole, role);
        if (!result.Success)
            return ValourResult.Problem(result.Message);

        return Results.Ok();
    }

    [ValourRoute(HttpVerbs.Delete, "api/planetroles/{id}")]
    [UserRequired(UserPermissionsEnum.PlanetManagement)]
    public static async Task<IResult> DeleteRouteAsync(
        long id,
        PlanetMemberService memberService,
        PlanetRoleService roleService)
    {
        var role = await roleService.GetAsync(id);

        // Get member
        var member = await memberService.GetCurrentAsync(role.PlanetId);
        if (member is null)
            return ValourResult.NotPlanetMember();

        if (!await memberService.HasPermissionAsync(member, PlanetPermissions.ManageRoles))
            return ValourResult.LacksPermission(PlanetPermissions.ManageRoles);

        var result = await roleService.DeleteAsync(role);
        if (!result.Success)
            return ValourResult.Problem(result.Message);

        return Results.NoContent();

    }
}