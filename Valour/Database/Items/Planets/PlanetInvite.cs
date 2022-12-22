using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Valour.Server.Database.Items;
using Valour.Server.Database.Items.Users;
using Valour.Shared;
using Valour.Shared.Authorization;
using Valour.Shared.Items.Planets;

namespace Valour.Database.Items.Planets;

[Table("planet_invites")]
public class PlanetInvite : Item, IPlanetItem, ISharedPlanetInvite
{
    [JsonIgnore]
    [ForeignKey("PlanetId")]
    public Planet Planet { get; set; }

    [Column("planet_id")]
    public long PlanetId { get; set; }
    
    [JsonIgnore]
    public override string BaseRoute =>
        $"api/planet/{{planetId}}/{nameof(PlanetInvite)}";

    /// <summary>
    /// The invite code
    /// </summary>
    [Column("code")]
    public string Code { get; set; }

    /// <summary>
    /// The user that created the invite
    /// </summary>
    [Column("issuer_id")]
    public long IssuerId { get; set; }

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


    public async Task<TaskResult> IsUserBanned(long userId, ValourDB db)
    {
        bool banned = await db.PlanetBans.AnyAsync(x => x.TargetId == userId && x.PlanetId == this.PlanetId);
        if (banned)
            return new TaskResult(false, "User is banned from the planet");

        return TaskResult.SuccessResult;
    }

    public async Task DeleteAsync(ValourDB db)
    {
        db.PlanetInvites.Remove(this);
    }

    [ValourRoute(HttpVerbs.Get, "/{inviteCode}", $"api/{nameof(PlanetInvite)}"), TokenRequired]
    public static async Task<IResult> GetRouteAsync(
        [FromRoute] string inviteCode, 
        ValourDB db)
    {
        var invite = await db.PlanetInvites.FirstOrDefaultAsync(x => x.Code == inviteCode);

        if (invite is null)
            return ValourResult.NotFound<PlanetInvite>();

        return Results.Json(invite);
    }

    [ValourRoute(HttpVerbs.Post), TokenRequired]
    [UserPermissionsRequired(UserPermissionsEnum.PlanetManagement)]
    [PlanetMembershipRequired(permissions: PlanetPermissionsEnum.Invite)]
    public static async Task<IResult> PostRouteAsync(
        [FromBody] PlanetInvite invite,
        HttpContext ctx,
        ValourDB db,
        CoreHubService hubService,
        ILogger<PlanetInvite> logger)
    {
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

        hubService.NotifyPlanetItemChange(invite);
        
        return Results.Created(invite.GetUri(), invite);

    }

    [ValourRoute(HttpVerbs.Put), TokenRequired]
    [UserPermissionsRequired(UserPermissionsEnum.PlanetManagement)]
    [PlanetMembershipRequired(permissions: PlanetPermissionsEnum.Manage)]
    public static async Task<IResult> PutRouteAsync(
        [FromBody] PlanetInvite invite, 
        long id, 
        HttpContext ctx,
        ValourDB db,
        CoreHubService hubService,
        ILogger<PlanetInvite> logger)
    {
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
            db.Entry(oldInvite).State = EntityState.Detached;
            db.PlanetInvites.Update(invite);
            await db.SaveChangesAsync();
        }
        catch (System.Exception e)
        {
            logger.LogError(e.Message);
            return Results.Problem(e.Message);
        }
        
        hubService.NotifyPlanetItemChange(invite);
        
        return Results.Json(invite);

    }

    [ValourRoute(HttpVerbs.Delete), TokenRequired]
    [UserPermissionsRequired(UserPermissionsEnum.PlanetManagement)]
    [PlanetMembershipRequired(permissions: PlanetPermissionsEnum.Manage)]
    public static async Task<IResult> DeleteRouteAsync(
        long id,
        ValourDB db,
        CoreHubService hubService,
        ILogger<PlanetInvite> logger)
    {
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
        
        hubService.NotifyPlanetItemDelete(invite);
        
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

    [ValourRoute(HttpVerbs.Get, "/{inviteCode}/planetname", $"api/{nameof(PlanetInvite)}")]
    public static async Task<IResult> GetPlanetName(
        string inviteCode, 
        ValourDB db)
    {
        var invite = await db.PlanetInvites.Include(x => x.Planet).FirstOrDefaultAsync(x => x.Code == inviteCode);

        return invite is null ? ValourResult.NotFound<PlanetInvite>() : Results.Json(invite.Planet.Name);
    }

    [ValourRoute(HttpVerbs.Get, "/{inviteCode}/planeticon", $"api/{nameof(PlanetInvite)}")]
    public static async Task<IResult> GetPlanetIconUrl(
        string inviteCode, 
        ValourDB db)
    {
        var invite = await db.PlanetInvites.Include(x => x.Planet).FirstOrDefaultAsync(x => x.Code == inviteCode);

        return invite is null ? ValourResult.NotFound<PlanetInvite>() : Results.Json(invite.Planet.IconUrl);
    }

    [ValourRoute(HttpVerbs.Post, "/{inviteCode}/join", $"api/{nameof(PlanetInvite)}"), TokenRequired]
    [UserPermissionsRequired(UserPermissionsEnum.Invites)]
    public static async Task<IResult> Join(
        string inviteCode, 
        HttpContext ctx, 
        ValourDB db,
        CoreHubService hubService)
    {
        var invite = await db.PlanetInvites.Include(x => x.Planet).FirstOrDefaultAsync(x => x.Code == inviteCode);
        if (invite == null)
            return ValourResult.NotFound<PlanetInvite>();

        var userId = ctx.GetToken().UserId;

        if (await db.PlanetBans.AnyAsync(x => x.TargetId == userId && x.PlanetId == invite.PlanetId))
            return Results.BadRequest("User is banned from the planet");

        if (await db.PlanetMembers.AnyAsync(x => x.UserId == userId && x.PlanetId == invite.PlanetId))
            return Results.BadRequest("User is already a member");

        if (!invite.Planet.Public)
            return Results.BadRequest("Planet is set to private"); // TODO: Support invites w/ specific users

        var result = await invite.Planet.AddMemberAsync(await User.FindAsync<User>(userId, db), db, hubService);

        return result.Success ? Results.Created(result.Data.GetUri(), result.Data) : Results.Problem(result.Message);
    }
}
