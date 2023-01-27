using Microsoft.AspNetCore.Mvc;
using StackExchange.Redis;
using System.Data;
using Valour.Client.Pages;
using Valour.Server.Database;
using Valour.Server.Services;
using Valour.Shared.Authorization;

namespace Valour.Server.Api.Dynamic;

public class PlanetInviteApi
{
    [ValourRoute(HttpVerbs.Get, "api/invites/{inviteCode}")]
    public static async Task<IResult> GetRouteAsync(
        string inviteCode, 
        PlanetInviteService inviteService)
    {
        var invite = await inviteService.GetAsync(inviteCode);

        if (invite is null)
            return ValourResult.NotFound<PlanetInvite>();

        return Results.Json(invite);
    }

    [ValourRoute(HttpVerbs.Post, "api/invites")]
    [UserRequired(UserPermissionsEnum.PlanetManagement)]
    public static async Task<IResult> PostRouteAsync(
        [FromBody] PlanetInvite invite,
        PlanetMemberService memberService,
        PlanetInviteService inviteService)
    {
        if (invite is null)
            return ValourResult.BadRequest("Include invite in body.");

        // Get member
        var member = await memberService.GetCurrentAsync(invite.PlanetId);
        if (member is null)
            return ValourResult.NotPlanetMember();

        if (!await memberService.HasPermissionAsync(member, PlanetPermissions.Invite))
            return ValourResult.LacksPermission(PlanetPermissions.Invite);

        var result = await inviteService.CreateAsync(invite, member);
        if (!result.Success)
            return ValourResult.Problem(result.Message);

        return Results.Created($"api/planetinvites/{result.Data.Id}", result.Data);
    }

    [ValourRoute(HttpVerbs.Put, "api/invites/{id}")]
    [UserRequired(UserPermissionsEnum.PlanetManagement)]
    public static async Task<IResult> PutRouteAsync(
        [FromBody] PlanetInvite invite,
        PlanetMemberService memberService,
        PlanetInviteService inviteService)
    {
        if (invite is null)
            return ValourResult.BadRequest("Include invite in body.");

        // Get member
        var member = await memberService.GetCurrentAsync(invite.PlanetId);
        if (member is null)
            return ValourResult.NotPlanetMember();

        if (!await memberService.HasPermissionAsync(member, PlanetPermissions.Manage))
            return ValourResult.LacksPermission(PlanetPermissions.Manage);

        var oldInvite = await inviteService.GetAsync(invite.Id);

        var result = await inviteService.UpdateAsync(oldInvite, invite);
        if (!result.Success)
            return ValourResult.Problem(result.Message);

        return Results.Json(result.Data);
    }

    [ValourRoute(HttpVerbs.Delete, "api/invites/{id}")]
    [UserRequired(UserPermissionsEnum.PlanetManagement)]
    public static async Task<IResult> DeleteRouteAsync(
        long id,
        PlanetMemberService memberService,
        PlanetInviteService inviteService)
    {
        var invite = await inviteService.GetAsync(id);

        // Get member
        var member = await memberService.GetCurrentAsync(invite.PlanetId);
        if (member is null)
            return ValourResult.NotPlanetMember();

        if (!await memberService.HasPermissionAsync(member, PlanetPermissions.Manage))
            return ValourResult.LacksPermission(PlanetPermissions.Manage);

        await inviteService.DeleteAsync(invite);
        
        return Results.NoContent();

    }
    
    private Random random = new();
    private const string inviteChars = "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789";

    private async Task<string> GenerateCode(ValourDB db)
    {
        
        string code;
        bool exists;

        do
        {
            code = new string(Enumerable.Repeat(inviteChars, 8).Select(s => s[random.Next(s.Length)]).ToArray());
            exists = await db.PlanetInvites.AnyAsync(x => x.Code == code);
        }
        while (exists);
        return code;
    }

    // Custom routes

    [ValourRoute(HttpVerbs.Get, "api/invites/{inviteCode}/name")]
    public static async Task<IResult> GetPlanetName(
        string inviteCode, 
        PlanetInviteService inviteService,
        PlanetService planetService)
    {
        var invite = await inviteService.GetAsync(inviteCode);

        return invite is null ? ValourResult.NotFound<PlanetInvite>() : Results.Json((await planetService.GetAsync(invite.PlanetId)).Name);
    }

    [ValourRoute(HttpVerbs.Get, "api/invites/{inviteCode}/icon")]
    public static async Task<IResult> GetPlanetIconUrl(
        string inviteCode, 
        PlanetInviteService inviteService,
        PlanetService planetService)
    {
        var invite = await inviteService.GetAsync(inviteCode);

        return invite is null ? ValourResult.NotFound<PlanetInvite>() : Results.Json((await planetService.GetAsync(invite.PlanetId)).IconUrl);
    }
}