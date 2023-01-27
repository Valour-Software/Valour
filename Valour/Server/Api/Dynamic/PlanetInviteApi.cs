using Microsoft.AspNetCore.Mvc;
using StackExchange.Redis;
using System.Data;
using Valour.Client.Pages;
using Valour.Server.Database;
using Valour.Shared.Authorization;

namespace Valour.Server.Api.Dynamic;

public class PlanetInviteApi
{
    [ValourRoute(HttpVerbs.Get, "api/planetinvites/{inviteCode}")]
    public static async Task<IResult> GetRouteAsync(
        string inviteCode, 
        PlanetInviteService inviteService)
    {
        var invite = await inviteService.GetAsync(inviteCode);

        if (invite is null)
            return ValourResult.NotFound<PlanetInvite>();

        return Results.Json(invite);
    }

    [ValourRoute(HttpVerbs.Post, "api/planetinvites")]
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

        return Results.Created($"api/planetinvites/{invite.Id}", invite);
    }

    [ValourRoute(HttpVerbs.Put, "api/planetinvites/{id}")]
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

        return Results.Json(invite);
    }

    [ValourRoute(HttpVerbs.Delete, "api/planetinvites/{id}")]
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

    [ValourRoute(HttpVerbs.Get, "api/planetinvites/{inviteCode}/planetname")]
    public static async Task<IResult> GetPlanetName(
        string inviteCode, 
        ValourDB db)
    {
        var invite = await db.PlanetInvites.Include(x => x.Planet).FirstOrDefaultAsync(x => x.Code == inviteCode);

        return invite is null ? ValourResult.NotFound<PlanetInvite>() : Results.Json(invite.Planet.Name);
    }

    [ValourRoute(HttpVerbs.Get, "api/planetinvites/{inviteCode}/planeticon")]
    public static async Task<IResult> GetPlanetIconUrl(
        string inviteCode, 
        ValourDB db)
    {
        var invite = await db.PlanetInvites.Include(x => x.Planet).FirstOrDefaultAsync(x => x.Code == inviteCode);

        return invite is null ? ValourResult.NotFound<PlanetInvite>() : Results.Json(invite.Planet.IconUrl);
    }

    [ValourRoute(HttpVerbs.Post, "api/planetinvites/{inviteCode}/join")]
    [UserRequired(UserPermissionsEnum.Invites)]
    public static async Task<IResult> Join(
        string inviteCode,
        ValourDB db,
        PlanetMemberService memberService,
        PlanetInviteService inviteService,
        UserService userService,
        PlanetService planetService)
    {
        Valour.Database.PlanetInvite invite = await db.PlanetInvites.Include(x => x.Planet).FirstOrDefaultAsync(x => x.Code == inviteCode);
        if (invite == null)
            return ValourResult.NotFound<PlanetInvite>();

        var user = await userService.GetCurrentUserAsync();
        var userId = user.Id;

        if (await db.PlanetBans.AnyAsync(x => x.TargetId == userId && x.PlanetId == invite.PlanetId))
            return Results.BadRequest("User is banned from the planet");

        if (await db.PlanetMembers.AnyAsync(x => x.UserId == userId && x.PlanetId == invite.PlanetId))
            return Results.BadRequest("User is already a member");

        if (!invite.Planet.Public)
            return Results.BadRequest("Planet is set to private"); // TODO: Support invites w/ specific users

        var result = await memberService.AddMemberAsync(invite.Planet.ToModel(), user);

        return result.Success ? Results.Created(result.Data.GetUri(), result.Data) : Results.Problem(result.Message);
    }
}