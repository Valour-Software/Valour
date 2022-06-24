using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json.Serialization;
using Valour.Database.Items.Planets.Members;
using Valour.Shared;
using Valour.Shared.Items;
using Microsoft.EntityFrameworkCore;
using Valour.Shared.Items.Planets;
using Valour.Shared.Authorization;
using Valour.Database.Items.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Builder;
using Valour.Shared.Http;
using Valour.Database.Attributes;
using System.Web.Mvc;
using Valour.Database.Extensions;
using Microsoft.Extensions.Logging;
using Valour.Database.Items.Users;

namespace Valour.Database.Items.Planets;

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
    public ulong Issuer_Id { get; set; }

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
        bool banned = await db.PlanetBans.AnyAsync(x => x.Target_Id == user_Id && x.Planet_Id == this.Planet_Id);
        if (banned)
            return new TaskResult(false, "User is banned from the planet");

        return TaskResult.SuccessResult;
    }

    public async Task DeleteAsync(ValourDB db)
    {
        db.PlanetInvites.Remove(this);

        await db.SaveChangesAsync();
    }

    [ValourRoute(HttpVerbs.Get), InjectDB]
    public static async Task<IResult> GetRouteAsync(HttpContext ctx, ulong id,
        ILogger<Invite> logger)
    {
        var db = ctx.GetDb();

        var invite = await FindAsync<Invite>(id, db);

        if (invite is null)
            return ValourResult.NotFound<Invite>();

        return Results.Json(invite);

    }

    [ValourRoute(HttpVerbs.Post), TokenRequired, InjectDB]
    [PlanetMembershipRequired]
    [PlanetPermsRequired(PlanetPermissionsEnum.Invite)]
    public static async Task<IResult> PostRouteAsync(HttpContext ctx, [FromBody] Invite invite,
        ILogger<Invite> logger)
    {
        var db = ctx.GetDb();
        var authMember = ctx.GetMember();

        invite.Issuer_Id = authMember.User_Id;
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

    [ValourRoute(HttpVerbs.Put), TokenRequired, InjectDB]
    [PlanetMembershipRequired]
    [PlanetPermsRequired(PlanetPermissionsEnum.Manage)]
    public static async Task<IResult> PutRouteAsync(HttpContext ctx, ulong id, [FromBody] Invite invite,
        ILogger<Invite> logger)
    {
        var db = ctx.GetDb();

        var oldInvite = await FindAsync<Invite>(id, db);

        if (invite.Code != oldInvite.Code)
            return Results.BadRequest("You cannot change the code.");
        if (invite.Issuer_Id != oldInvite.Issuer_Id)
            return Results.BadRequest("You cannot change who issued.");
        if (invite.Created != oldInvite.Created)
            return Results.BadRequest("You cannot change the creation time.");
        if (invite.Planet_Id != oldInvite.Planet_Id)
            return Results.BadRequest("You cannot change what planet.");

        try
        {
            db.PlanetInvites.Update(invite);
            await db.SaveChangesAsync();
        }
        catch(System.Exception e)
        {
            logger.LogError(e.Message);
            return Results.Problem(e.Message);
        }

        PlanetHub.NotifyPlanetItemChange(invite);

        return Results.Json(invite);

    }

    [ValourRoute(HttpVerbs.Delete), TokenRequired, InjectDB]
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
        }
        catch(System.Exception e)
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

    [ValourRoute(HttpVerbs.Get, "/{invite_code}/planetname"), InjectDB]
    public async Task<IResult> GetPlanetName(HttpContext ctx, string invite_code)
    {
        var db = ctx.GetDb();

        var invite = await db.PlanetInvites.Include(x => x.Planet).FirstOrDefaultAsync(x => x.Code == invite_code);

        if (invite is null)
            return ValourResult.NotFound<Invite>();

        return Results.Ok(invite.Planet.Name);
    }

    [ValourRoute(HttpVerbs.Get, "/{invite_code}/planeticon"), InjectDB]
    public async Task<IResult> GetPlanetIconUrl(HttpContext ctx, string invite_code)
    {
        var db = ctx.GetDb();

        var invite = await db.PlanetInvites.Include(x => x.Planet).FirstOrDefaultAsync(x => x.Code == invite_code);

        if (invite is null)
            return ValourResult.NotFound<Invite>();

        return Results.Ok(invite.Planet.IconUrl);
    }

    [ValourRoute(HttpVerbs.Post, "/{invite_code}/join"), TokenRequired, InjectDB]
    public async Task<IResult> Join(HttpContext ctx, string invite_code)
    {
        var db = ctx.GetDb();

        var invite = await db.PlanetInvites.Include(x => x.Planet).FirstOrDefaultAsync(x => x.Code == invite_code);
        if (invite == null)
            return ValourResult.NotFound<Invite>();

        ulong user_id = ctx.GetToken().User_Id;

        if (await db.PlanetBans.AnyAsync(x => x.Target_Id == user_id && x.Planet_Id == invite.Planet_Id))
            return Results.BadRequest("User is banned from the planet");

        if (await db.PlanetMembers.AnyAsync(x => x.User_Id == user_id && x.Planet_Id == invite.Planet_Id))
            return Results.BadRequest("User is already a member");

        if (!invite.Planet.Public)
            return Results.BadRequest("Planet is set to private"); // TODO: Support invites w/ specific users

        TaskResult<PlanetMember> result =  await invite.Planet.AddMemberAsync(await User.FindAsync<User>(user_id, db), db);

        if (result.Success)
            return Results.Created(result.Data.GetUri(), result.Data);
        else
            return Results.Problem(result.Message);
    }
}
