using Microsoft.AspNetCore.Mvc;
using Valour.Server.Database.Items.Planets.Members;
using Valour.Server.Database.Items.Users;
using Valour.Shared;
using Valour.Shared.Authorization;
using Valour.Shared.Items.Planets;

namespace Valour.Server.Database.Items.Planets;

[Table("planet_invites")]
public class PlanetInvite : PlanetItem, ISharedPlanetInvite
{
    /// <summary>
    /// The invite code
    /// </summary>
    [Column("code")]
    public string Code { get; set; }

    /// <summary>
    /// The user that created the invite
    /// </summary>
    [Column("issuer_id")]
    public ulong IssuerId { get; set; }

    /// <summary>
    /// The time the invite was created
    /// </summary>
    [Column("time_created")]
    public DateTime TimeCreated { get; set; }

    /// <summary>
    /// When the invite expires
    /// </summary>
    [Column("time_expires")]
    public DateTime? TimeExpires { get; set; }

    public bool IsPermanent() => TimeExpires is null;


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

    [ValourRoute(HttpVerbs.Get, "/{code}"), TokenRequired, InjectDb]
    public static async Task<IResult> GetRouteAsync(HttpContext ctx, string code)
    {
        var db = ctx.GetDb();

        var invite = await FindAsync<PlanetInvite>(code, db);

        if (invite is null)
            return ValourResult.NotFound<PlanetInvite>();

        return Results.Json(invite);
    }

    [ValourRoute(HttpVerbs.Post), TokenRequired, InjectDb]
    [UserPermissionsRequired(UserPermissionsEnum.PlanetManagement)]
    [PlanetMembershipRequired]
    [PlanetPermsRequired(PlanetPermissionsEnum.Invite)]
    public static async Task<IResult> PostRouteAsync(HttpContext ctx, [FromBody] PlanetInvite invite,
        ILogger<PlanetInvite> logger)
    {
        var db = ctx.GetDb();
        var authMember = ctx.GetMember();

        invite.Id = IdManager.Generate();
        invite.IssuerId = authMember.UserId;
        invite.TimeCreated = DateTime.UtcNow;
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
    public static async Task<IResult> PutRouteAsync(HttpContext ctx, ulong id, [FromBody] PlanetInvite invite,
        ILogger<PlanetInvite> logger)
    {
        var db = ctx.GetDb();

        var oldInvite = await FindAsync<PlanetInvite>(id, db);

        if (invite.Code != oldInvite.Code)
            return Results.BadRequest("You cannot change the code.");
        if (invite.IssuerId != oldInvite.IssuerId)
            return Results.BadRequest("You cannot change who issued.");
        if (invite.TimeCreated != oldInvite.TimeCreated)
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
        ILogger<PlanetInvite> logger)
    {
        var db = ctx.GetDb();

        var invite = await FindAsync<PlanetInvite>(id, db);

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

    [ValourRoute(HttpVerbs.Get, "/{inviteCode}/planetname"), InjectDb]
    public static async Task<IResult> GetPlanetName(HttpContext ctx, string inviteCode)
    {
        var db = ctx.GetDb();

        var invite = await db.PlanetInvites.Include(x => x.Planet).FirstOrDefaultAsync(x => x.Code == inviteCode);

        if (invite is null)
            return ValourResult.NotFound<PlanetInvite>();

        return Results.Ok(invite.Planet.Name);
    }

    [ValourRoute(HttpVerbs.Get, "/{inviteCode}/planeticon"), InjectDb]
    public static async Task<IResult> GetPlanetIconUrl(HttpContext ctx, string inviteCode)
    {
        var db = ctx.GetDb();

        var invite = await db.PlanetInvites.Include(x => x.Planet).FirstOrDefaultAsync(x => x.Code == inviteCode);

        if (invite is null)
            return ValourResult.NotFound<PlanetInvite>();

        return Results.Ok(invite.Planet.IconUrl);
    }

    [ValourRoute(HttpVerbs.Post, "/{inviteCode}/join"), TokenRequired, InjectDb]
    [UserPermissionsRequired(UserPermissionsEnum.Invites)]
    public static async Task<IResult> Join(HttpContext ctx, string inviteCode)
    {
        var db = ctx.GetDb();

        var invite = await db.PlanetInvites.Include(x => x.Planet).FirstOrDefaultAsync(x => x.Code == inviteCode);
        if (invite == null)
            return ValourResult.NotFound<PlanetInvite>();

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
