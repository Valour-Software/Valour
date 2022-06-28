﻿using Microsoft.AspNetCore.Mvc;
using Valour.Server.Database.Items.Planets.Members;
using Valour.Server.Database.Items.Users;
using Valour.Shared;
using Valour.Shared.Authorization;
using Valour.Shared.Http;
using Valour.Shared.Items;

namespace Valour.Server.Database.Items.Planets;

public class Invite : PlanetItem
{
    public override ItemType ItemType => ItemType.PlanetInvite;

    /// <summary>
    /// The invite code
    /// </summary>
    public string Code { get; set; }

    /// <summary>
    /// The user that created the invite
    /// </summary>
    public ulong IssuerId { get; set; }

    /// <summary>
    /// The time the invite was created
    /// </summary>
    public DateTime Created { get; set; }

    /// <summary>
    /// When the invite expires
    /// </summary>
    public DateTime? Expires { get; set; }

    public bool IsPermanent() => Expires is null;

    public async Task<TaskResult> IsUserBanned(ulong user_Id, ValourDB db)
    {
        bool banned = await db.PlanetBans.AnyAsync(x => x.TargetId == user_Id && x.PlanetId == this.PlanetId);
        if (banned)
            return new TaskResult(false, "User is banned from the planet");

        return TaskResult.SuccessResult;
    }

    public async Task DeleteAsync(ValourDB db)
    {
        db.PlanetInvites.Remove(this);
    }

    [ValourRoute(HttpVerbs.Get), TokenRequired, InjectDb]
    public static async Task<IResult> GetRouteAsync(HttpContext ctx, ulong id)
    {
        var db = ctx.GetDb();

        var invite = await FindAsync<Invite>(id, db);

        if (invite is null)
            return ValourResult.NotFound<Invite>();

        return Results.Json(invite);
    }

    [ValourRoute(HttpVerbs.Post), TokenRequired, InjectDb]
    [UserPermissionsRequired(UserPermissionsEnum.PlanetManagement)]
    [PlanetMembershipRequired]
    [PlanetPermsRequired(PlanetPermissionsEnum.Invite)]
    public static async Task<IResult> PostRouteAsync(HttpContext ctx, [FromBody] Invite invite,
        ILogger<Invite> logger)
    {
        var db = ctx.GetDb();
        var authMember = ctx.GetMember();

        invite.IssuerId = authMember.UserId;
        invite.Created = DateTime.UtcNow;
        invite.Code = await invite.GenerateCode(db);

        try
        {
            await db.AddAsync(invite);
            await db.SaveChangesAsync();
        }
        catch (System.Exception e)
        {
            logger.LogError(e.Message);
            return Results.Problem(e.Message);
        }

        PlanetHub.NotifyPlanetItemChange(invite);

        return Results.NoContent();

    }

    [ValourRoute(HttpVerbs.Put), TokenRequired, InjectDb]
    [UserPermissionsRequired(UserPermissionsEnum.PlanetManagement)]
    [PlanetMembershipRequired]
    [PlanetPermsRequired(PlanetPermissionsEnum.Manage)]
    public static async Task<IResult> PutRouteAsync(HttpContext ctx, ulong id, [FromBody] Invite invite,
        ILogger<Invite> logger)
    {
        var db = ctx.GetDb();

        var oldInvite = await FindAsync<Invite>(id, db);

        if (invite.Code != oldInvite.Code)
            return Results.BadRequest("You cannot change the code.");
        if (invite.IssuerId != oldInvite.IssuerId)
            return Results.BadRequest("You cannot change who issued.");
        if (invite.Created != oldInvite.Created)
            return Results.BadRequest("You cannot change the creation time.");
        if (invite.PlanetId != oldInvite.PlanetId)
            return Results.BadRequest("You cannot change what planet.");

        try
        {
            db.PlanetInvites.Update(invite);
            await db.SaveChangesAsync();
        }
        catch (System.Exception e)
        {
            logger.LogError(e.Message);
            return Results.Problem(e.Message);
        }

        PlanetHub.NotifyPlanetItemChange(invite);

        return Results.Json(invite);

    }

    [ValourRoute(HttpVerbs.Delete), TokenRequired, InjectDb]
    [UserPermissionsRequired(UserPermissionsEnum.PlanetManagement)]
    [PlanetMembershipRequired]
    [PlanetPermsRequired(PlanetPermissionsEnum.Manage)]
    public static async Task<IResult> DeleteRouteAsync(HttpContext ctx, ulong id,
        ILogger<Invite> logger)
    {
        var db = ctx.GetDb();

        var invite = await FindAsync<Invite>(id, db);

        try
        {
            await invite.DeleteAsync(db);
            await db.SaveChangesAsync();
        }
        catch (System.Exception e)
        {
            logger.LogError(e.Message);
            return Results.Problem(e.Message);
        }

        PlanetHub.NotifyPlanetItemDelete(invite);

        return Results.NoContent();

    }

    public async Task<string> GenerateCode(ValourDB db)
    {
        Random random = new();

        const string chars = "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789";
        string code = "";

        bool exists = false;

        do
        {
            code = new string(Enumerable.Repeat(chars, 8).Select(s => s[random.Next(s.Length)]).ToArray());
            exists = await db.PlanetInvites.AnyAsync(x => x.Code == code);
        }
        while (exists);
        return code;
    }

    // Custom routes

    [ValourRoute(HttpVerbs.Get, "/{invite_code}/planetname"), InjectDb]
    public async Task<IResult> GetPlanetName(HttpContext ctx, string invite_code)
    {
        var db = ctx.GetDb();

        var invite = await db.PlanetInvites.Include(x => x.Planet).FirstOrDefaultAsync(x => x.Code == invite_code);

        if (invite is null)
            return ValourResult.NotFound<Invite>();

        return Results.Ok(invite.Planet.Name);
    }

    [ValourRoute(HttpVerbs.Get, "/{invite_code}/planeticon"), InjectDb]
    public async Task<IResult> GetPlanetIconUrl(HttpContext ctx, string invite_code)
    {
        var db = ctx.GetDb();

        var invite = await db.PlanetInvites.Include(x => x.Planet).FirstOrDefaultAsync(x => x.Code == invite_code);

        if (invite is null)
            return ValourResult.NotFound<Invite>();

        return Results.Ok(invite.Planet.IconUrl);
    }

    [ValourRoute(HttpVerbs.Post, "/{invite_code}/join"), TokenRequired, InjectDb]
    [UserPermissionsRequired(UserPermissionsEnum.Invites)]
    public async Task<IResult> Join(HttpContext ctx, string invite_code)
    {
        var db = ctx.GetDb();

        var invite = await db.PlanetInvites.Include(x => x.Planet).FirstOrDefaultAsync(x => x.Code == invite_code);
        if (invite == null)
            return ValourResult.NotFound<Invite>();

        ulong userId = ctx.GetToken().UserId;

        if (await db.PlanetBans.AnyAsync(x => x.TargetId == userId && x.PlanetId == invite.PlanetId))
            return Results.BadRequest("User is banned from the planet");

        if (await db.PlanetMembers.AnyAsync(x => x.UserId == userId && x.PlanetId == invite.PlanetId))
            return Results.BadRequest("User is already a member");

        if (!invite.Planet.Public)
            return Results.BadRequest("Planet is set to private"); // TODO: Support invites w/ specific users

        TaskResult<PlanetMember> result = await invite.Planet.AddMemberAsync(await User.FindAsync<User>(userId, db), db);

        if (result.Success)
            return Results.Created(result.Data.GetUri(), result.Data);
        else
            return Results.Problem(result.Message);
    }
}