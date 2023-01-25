using Microsoft.AspNetCore.Mvc;
using Valour.Shared;
using Valour.Shared.Authorization;
using Valour.Server.Models;

namespace Valour.Server.Api.Dynamic;

public class PlanetMemberApi
{
     // Helpful route to return the member for the authorizing user
    [ValourRoute(HttpVerbs.Get, "api/members/self/{planetId}")]
    [UserRequired(UserPermissionsEnum.Membership)]
    public static async Task<IResult> GetSelfRouteAsync(
        long planetId, 
        PlanetMemberService memberService)
    {
        var member = await memberService.GetCurrentAsync(planetId);
        if (member is null)
            return ValourResult.NotFound("Member not found");

        return Results.Json(member);
    }
        

    [ValourRoute(HttpVerbs.Get, "api/members/{id}")]
    [UserRequired(UserPermissionsEnum.Membership)]
    public static async Task<IResult> GetRouteAsync(
        long id, 
        PlanetMemberService service)
    {
        // Get other member
        var member = await service.GetAsync(id);
        if (member is null)
            return ValourResult.NotFound("Member not found");
        
        // Need to be a member to see other members
        var self = await service.GetCurrentAsync(member.PlanetId);
        if (self is null)
            return ValourResult.NotPlanetMember();
        
        return Results.Json(member);
    }

    [ValourRoute(HttpVerbs.Get, "api/members/{id}/authority")]
    [UserRequired(UserPermissionsEnum.Membership)]
    public static async Task<IResult> GetAuthorityRouteAsync(
        long id, 
        PlanetMemberService service)
    {
        // Get other member
        var member = await service.GetAsync(id);
        if (member is null)
            return ValourResult.NotFound("Member not found");
        
        // Need to be a member to see other members
        var self = await service.GetCurrentAsync(member.PlanetId);
        if (self is null)
            return ValourResult.NotPlanetMember();

        var authority = await service.GetAuthorityAsync(member);
        
        return Results.Json(authority);
    }

    [ValourRoute(HttpVerbs.Get, "api/members/byuser/{planetId}/{userId}")]
    [UserRequired(UserPermissionsEnum.Membership)]
    public static async Task<IResult> GetRoute(
        long planetId, 
        long userId,
        PlanetMemberService service)
    {
        var member = await service.GetByUserAsync(userId, planetId);
        if (member is null)
            return ValourResult.NotFound("Member not found");
        
        // Need to be a member to see other members
        var self = await service.GetCurrentAsync(planetId);
        if (self is null)
            return ValourResult.NotPlanetMember();

        return Results.Json(member);
    }

    [ValourRoute(HttpVerbs.Put, "api/members/{id}")]
    [UserRequired(UserPermissionsEnum.Membership)]
    public static async Task<IResult> PutRouteAsync(
        [FromBody] PlanetMember member, 
        long id, 
        PlanetMemberService service,
        UserService userService)
    {
        var selfId = await userService.GetCurrentUserId();
        
        if (selfId != member.UserId)
            return Results.BadRequest("You can only modify your own membership.");

        await service.UpdateAsync(member);

        return Results.Ok(member);
    }

    [ValourRoute(HttpVerbs.Delete, "api/members/{id}")]
    [UserRequired(UserPermissionsEnum.Membership)]
    public static async Task<IResult> DeleteRouteAsync(
        long id,
        PlanetMemberService memberService)
    {
        var targetMember = await memberService.GetAsync(id);
        if (targetMember is null)
            return ValourResult.NotFound("Target member not found.");
        
        var selfMember = await memberService.GetCurrentAsync(targetMember.PlanetId);
        if (selfMember is null)
            return ValourResult.NotPlanetMember();

        // You can always delete your own membership, so we only check permissions
        // if you are not the same as the target
        if (selfMember.UserId != targetMember.Id)
        {
            if (!await memberService.HasPermissionAsync(selfMember, PlanetPermissions.Kick))
                return ValourResult.LacksPermission(PlanetPermissions.Kick);

            if (await memberService.GetAuthorityAsync(selfMember) < await memberService.GetAuthorityAsync(targetMember))
                return ValourResult.Forbid("You have less authority than the target member.");
        }

        await memberService.DeleteAsync(targetMember);

        return Results.NoContent();
    }

    private static TaskResult ValidateName(PlanetMember member)
    {
        // Ensure nickname is valid
        return member.Nickname.Length > 32 ? new TaskResult(false, "Maximum nickname is 32 characters.") : 
            TaskResult.SuccessResult;
    }

    [ValourRoute(HttpVerbs.Get, "/{id}/roles"), TokenRequired]
    [PlanetMembershipRequired]
    public static async Task<IResult> GetAllRolesForMember(long id, long planetId, ValourDB db)
    {
        var member = await db.PlanetMembers.Include(x => x.RoleMembership.OrderBy(x => x.Role.Position))
                                           .ThenInclude(x => x.Role)
                                           .FirstOrDefaultAsync(x => x.Id == id && x.PlanetId == planetId);
        
        return member is null ? ValourResult.NotFound<PlanetMember>() : 
            Results.Json(member.RoleMembership.Select(r => r.RoleId));
    }

    [ValourRoute(HttpVerbs.Post, "/{id}/roles/{roleId}"), TokenRequired]
    [PlanetMembershipRequired]
    public static async Task<IResult> AddRoleToMember(
        long id, 
        long planetId, 
        long roleId, 
        HttpContext ctx,
        ValourDB db,
        CoreHubService hubService,
        PlanetMemberService memberService,
        PermissionsService permService,
        ILogger<PlanetMember> logger)
    {
        var authMember = ctx.GetMember();

        var member = await FindAsync<PlanetMember>(id, db);
        if (member is null)
            return ValourResult.NotFound<PlanetMember>();

        if (member.PlanetId != planetId)
            return ValourResult.NotFound<PlanetMember>();
        
        if (!await memberService.HasPermissionAsync(authMember, PlanetPermissions.ManageRoles))
            return ValourResult.LacksPermission(PlanetPermissions.ManageRoles);

        if (await db.PlanetRoleMembers.AnyAsync(x => x.MemberId == member.Id && x.RoleId == roleId))
            return Results.BadRequest("The member already has this role");

        var role = await db.PlanetRoles.FindAsync(roleId);
        if (role is null)
            return ValourResult.NotFound<PlanetRole>();

        var authAuthority = await memberService.GetAuthorityAsync(authMember);
        if (role.GetAuthority() > authAuthority)
            return ValourResult.Forbid("You have lower authority than the role you are trying to add");

        PlanetRoleMember newRoleMember = new()
        {
            Id = IdManager.Generate(),
            MemberId = member.Id,
            RoleId = roleId,
            UserId = member.UserId,
            PlanetId = member.PlanetId
        };

        try
        {
            await db.PlanetRoleMembers.AddAsync(newRoleMember);
            await db.SaveChangesAsync();
        }
        catch (System.Exception e)
        {
            logger.LogError(e.Message);
            return Results.Problem(e.Message);
        }

        hubService.NotifyPlanetItemChange(newRoleMember);

        return Results.Created(newRoleMember.GetUri(), newRoleMember);
    }


    [ValourRoute(HttpVerbs.Delete, "/{id}/roles/{roleId}"), TokenRequired]
    [PlanetMembershipRequired]
    public static async Task<IResult> RemoveRoleFromMember(
        long id, 
        long planetId, 
        long roleId, 
        HttpContext ctx,
        ValourDB db,
        CoreHubService hubService,
        PermissionsService permService,
        PlanetMemberService memberService,
        ILogger<PlanetMember> logger)
    {
        var authMember = ctx.GetMember();

        var member = await FindAsync<PlanetMember>(id, db);
        if (member is null)
            return ValourResult.NotFound<PlanetMember>();

        if (member.PlanetId != planetId)
            return ValourResult.NotFound<PlanetMember>();

        if (!await authMember.HasPermissionAsync(PlanetPermissions.ManageRoles, permService))
            return ValourResult.LacksPermission(PlanetPermissions.ManageRoles);

        var oldRoleMember = await db.PlanetRoleMembers.FirstOrDefaultAsync(x => x.MemberId == member.Id && x.RoleId == roleId);

        if (oldRoleMember is null)
            return Results.BadRequest("The member does not have this role");

        var role = await db.PlanetRoles.FindAsync(roleId);
        if (role is null)
            return ValourResult.NotFound<PlanetRole>();

        var authAuthority = await authMember.GetAuthorityAsync(memberService);
        if (role.GetAuthority() > authAuthority)
            return ValourResult.Forbid("You have less authority than the role you are trying to remove"); ;

        try
        {
            db.PlanetRoleMembers.Remove(oldRoleMember);
            await db.SaveChangesAsync();
        }
        catch (System.Exception e)
        {
            logger.LogError(e.Message);
            return Results.Problem(e.Message);
        }

        hubService.NotifyPlanetItemDelete(oldRoleMember);

        return Results.NoContent();
    }
}