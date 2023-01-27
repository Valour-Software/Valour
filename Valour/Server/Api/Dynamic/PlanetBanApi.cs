using Microsoft.AspNetCore.Mvc;
using Valour.Shared.Authorization;
using Valour.TenorTwo.Models;

namespace Valour.Server.Api.Dynamic;

public class PlanetBanApi
{
    [ValourRoute(HttpVerbs.Get, "api/planetbans/{id}")]
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

    [ValourRoute(HttpVerbs.Post, "api/planetbans")]
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

        if (!await memberService.HasPermissionAsync(member, PlanetPermissions.Ban))
            return ValourResult.LacksPermission(PlanetPermissions.Ban);

        return await banService.CreateAsync(ban, member);
    }

    [ValourRoute(HttpVerbs.Put, "api/planetbans/{id}")]
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

        return await banService.PutAsync(old, ban);
    }

    [ValourRoute(HttpVerbs.Delete, "api/planetbans/{id}")]
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

        return await banService.DeleteAsync(ban, member);
    }
}