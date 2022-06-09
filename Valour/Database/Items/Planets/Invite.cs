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
    public DateTime Creation_Time { get; set; }

    /// <summary>
    /// The length of the invite before its invaild
    /// </summary>
    public int? Hours { get; set; }

    public bool IsPermanent() => Hours is null;

    public async Task<TaskResult> IsUserBanned(ulong user_Id, ValourDB db)
    {
        bool banned = await db.PlanetBans.AnyAsync(x => x.Target_Id == user_Id && x.Planet_Id == this.Planet_Id);
        if (banned)
            return new TaskResult(false, "User is banned from the planet");

        return TaskResult.SuccessResult;
    }

    public override async Task<TaskResult> CanGetAsync(AuthToken token, PlanetMember member, ValourDB db) 
        => !await member.HasPermissionAsync(PlanetPermissions.Invite, db)
            ? new TaskResult(false, "Member lacks Planet Permission" + PlanetPermissions.Invite.Name)
            : TaskResult.SuccessResult;

    public override async Task<TaskResult> CanDeleteAsync(AuthToken token, PlanetMember member, ValourDB db)
        => await CanGetAsync(token, member, db);

    public override async Task<TaskResult> CanUpdateAsync(AuthToken token, PlanetMember member, PlanetItem old, ValourDB db)
    {
        TaskResult canGet = await CanGetAsync(token, member, db);
        if (!canGet.Success)
            return canGet;

        var oldInvite = old as Invite;

        if (this.Code != oldInvite.Code)
            return await Task.FromResult(new TaskResult(false, "You cannot change the code"));
        if (this.Issuer_Id != oldInvite.Issuer_Id)
            return await Task.FromResult(new TaskResult(false, "You cannot change who issued"));
        if (this.Creation_Time != oldInvite.Creation_Time)
            return await Task.FromResult(new TaskResult(false, "You cannot change the creation time"));

        this.Issuer_Id = member.User_Id;
        return await Task.FromResult(TaskResult.SuccessResult);
    }


    public override async Task<TaskResult> CanCreateAsync(AuthToken token, PlanetMember member, ValourDB db)
    {
        TaskResult canGet = await CanGetAsync(token, member, db);
        if (!canGet.Success)
            return canGet;

        this.Issuer_Id = member.User_Id;
        this.Creation_Time = DateTime.UtcNow;
        this.Code = await GenerateCode(db);

        return TaskResult.SuccessResult;
    }

    private static async Task<string> GenerateCode(ValourDB db)
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

    public override void RegisterCustomRoutes(WebApplication app)
    {
        app.MapGet(IdRoute + "/PlanetName", GetPlanetName);
        app.MapGet(IdRoute + "/PlanetIcon", GetPlanetIconUrl);
        app.MapPost(BaseRoute + "/Join", Join);
    }

    // Custom routes
    private static async Task<IResult> GetPlanetName(ValourDB db, string invite_code, [FromHeader] string authorization)
    {
        var authToken = await AuthToken.TryAuthorize(authorization, db);
        if (authToken == null)
            return Results.Unauthorized();

        var invite = await db.PlanetInvites.Include(x => x.Planet).FirstOrDefaultAsync(x => x.Code == invite_code);

        if (invite is null)
            return Results.NotFound();

        return Results.Ok(invite.Planet.Name);
    }

    private static async Task<IResult> GetPlanetIconUrl(ValourDB db, string invite_code, [FromHeader] string authorization)
    {
        var authToken = await AuthToken.TryAuthorize(authorization, db);
        if (authToken == null)
            return Results.Unauthorized();

        var invite = await db.PlanetInvites.Include(x => x.Planet).FirstOrDefaultAsync(x => x.Code == invite_code);

        if (invite is null)
            return Results.NotFound();

        return Results.Ok(invite.Planet.Image_Url);
    }

    private static async Task<IResult> Join(ValourDB db, string invite_code, [FromHeader] string authorization)
    {
        var authToken = await AuthToken.TryAuthorize(authorization, db);
        if (authToken == null)
            return Results.Unauthorized();

        var invite = await db.PlanetInvites.Include(x => x.Planet).FirstOrDefaultAsync(x => x.Code == invite_code);
        if (invite == null)
            return Results.NotFound();

        if (await db.PlanetBans.AnyAsync(x => x.Target_Id == authToken.User_Id && x.Planet_Id == invite.Planet_Id))
            return Results.BadRequest("User is banned from the planet");

        if (await db.PlanetMembers.AnyAsync(x => x.User_Id == authToken.User_Id && x.Planet_Id == invite.Planet_Id))
            return Results.BadRequest("User is already a member");

        if (!invite.Planet.Public)
            return Results.BadRequest("Planet is set to private"); // TODO: Support invites w/ specific users

        var user = await db.Users.FindAsync(authToken.User_Id);

        TaskResult<PlanetMember> result =  await invite.Planet.AddMemberAsync(user, db);

        if (result.Success)
            return Results.Created(result.Data.GetUri(), result.Data);
        else
            return Results.Problem(result.Message);
    }
}
