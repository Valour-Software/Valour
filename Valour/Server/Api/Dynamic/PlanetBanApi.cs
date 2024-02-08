using Microsoft.AspNetCore.Mvc;
using Valour.Shared.Authorization;
using Valour.TenorTwo.Models;

namespace Valour.Server.Api.Dynamic;

public class PlanetBanApi
{
    [ValourRoute(HttpVerbs.Get, "api/bans/{id}")]
    [UserRequired(UserPermissionsEnum.Membership)]
    //[PlanetPermsRequired(PlanetPermissionsEnum.Ban)] (There is an exception to this!)
    public static async Task<IResult> GetRoute(
        long id, 
        PlanetBanService banService,
        PlanetMemberService memberService)
    {
        var ban = await banService.GetAsync(id);

        if (ban is null)
            return ValourResult.NotFound<PlanetBan>();

        // Get member
        var member = await memberService.GetCurrentAsync(ban.PlanetId);
        if (member is null)
            return ValourResult.NotPlanetMember();

        // You can retrieve your own ban
        if (ban.TargetId != member.Id)
        {
            if (!await memberService.HasPermissionAsync(member, PlanetPermissions.Ban))
                return ValourResult.LacksPermission(PlanetPermissions.Ban);
        }

        return Results.Json(ban);
    }

    [ValourRoute(HttpVerbs.Post, "api/bans")]
    [UserRequired(UserPermissionsEnum.PlanetManagement)]
    public static async Task<IResult> PostRoute(
        [FromBody] PlanetBan ban, 
        PlanetMemberService memberService,
        PlanetBanService banService)
    {
        if (ban is null)
            return Results.BadRequest("Include ban in body.");

        // Get member
        var member = await memberService.GetCurrentAsync(ban.PlanetId);
        if (member is null)
            return ValourResult.NotPlanetMember();

        if (ban.TargetId == member.UserId)
            return ValourResult.BadRequest("You cannot ban yourself.");
        
        if (!await memberService.HasPermissionAsync(member, PlanetPermissions.Ban))
            return ValourResult.LacksPermission(PlanetPermissions.Ban);

        // Ensure user has more authority than the user being banned
        var target = await memberService.GetByUserAsync(ban.TargetId, ban.PlanetId);

        if (target is null)
            return ValourResult.NotFound<PlanetMember>();

        if (await memberService.GetAuthorityAsync(target) >= await memberService.GetAuthorityAsync(member))
            return ValourResult.Forbid("The target has a higher authority than you.");

        var result = await banService.CreateAsync(ban, member);
        if (!result.Success)
            return ValourResult.Problem(result.Message);

        return Results.Created($"api/bans/{result.Data.Id}", result.Data);
    }

    [ValourRoute(HttpVerbs.Put, "api/bans/{id}")]
    [UserRequired(UserPermissionsEnum.PlanetManagement)]
    public static async Task<IResult> PutRoute(
        [FromBody] PlanetBan ban, 
        long id,
        PlanetMemberService memberService,
        PlanetBanService banService)
    {
        if (ban is null)
            return Results.BadRequest("Include updated ban in body.");

        // Get member
        var member = await memberService.GetCurrentAsync(ban.PlanetId);
        if (member is null)
            return ValourResult.NotPlanetMember();

        if (!await memberService.HasPermissionAsync(member, PlanetPermissions.Ban))
            return ValourResult.LacksPermission(PlanetPermissions.Ban);

        var old = await banService.GetAsync(id);

        if (old is null)
            return ValourResult.NotFound<PlanetBan>();

        var result = await banService.PutAsync(ban);
        if (!result.Success)
            return ValourResult.Problem(result.Message);

        return Results.Json(result.Data);
    }

    [ValourRoute(HttpVerbs.Delete, "api/bans/{id}")]
    [UserRequired(UserPermissionsEnum.PlanetManagement)]
    public static async Task<IResult> DeleteRoute(
        long id, 
        PlanetMemberService memberService,
        PlanetBanService banService)
    {
        // Get ban
        var ban = await banService.GetAsync(id);

        // Get member
        var member = await memberService.GetCurrentAsync(ban.PlanetId);
        if (member is null)
            return ValourResult.NotPlanetMember();

        if (!await memberService.HasPermissionAsync(member, PlanetPermissions.Ban))
            return ValourResult.LacksPermission(PlanetPermissions.Ban);

        var result = await banService.DeleteAsync(ban, member);
        if (!result.Success)
            return ValourResult.Problem(result.Message);

        return Results.NoContent();
    }
}