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

    [ValourRoute(HttpVerbs.Delete), TokenRequired]
    [PlanetMembershipRequired(permissions: PlanetPermissionsEnum.Ban)]
    public static async Task<IResult> DeleteRoute(
        long id, 
        long planetId, 
        HttpContext ctx,
        ValourDB db,
        CoreHubService hubService,
        ILogger<PlanetBan> logger)
    {
        var member = ctx.GetMember();

        var ban = await FindAsync<PlanetBan>(id, db);

        // Ensure the user unbanning is either the user that made the ban, or someone
        // with equal or higher authority to them

        if (ban.IssuerId != member.Id)
        {
            var banner = await FindAsync<PlanetMember>(ban.IssuerId, db);

            if (await banner.GetAuthorityAsync(db) > await member.GetAuthorityAsync(db))
                return ValourResult.Forbid("The banner of this user has higher authority than you.");
        }

        try
        {
            db.PlanetBans.Remove(ban);
            await db.SaveChangesAsync();
        }
        catch (System.Exception e)
        {
            logger.LogError(e.Message);
            return Results.Problem(e.Message);
        }


        // Notify of changes
        hubService.NotifyPlanetItemDelete(ban);

        return Results.NoContent();
    }
}