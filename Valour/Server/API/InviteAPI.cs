using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;
using Valour.Database;
using Valour.Database.Items.Authorization;
using Valour.Database.Items.Planets;
using Valour.Shared.Oauth;

namespace Valour.Server.API;
public class InviteAPI : BaseAPI
{
    public static void AddRoutes(WebApplication app)
    {
        app.MapPost("api/invite", CreateInvite);

        app.MapGet("api/invite/{invite_code}", GetInvite);
        app.MapGet("api/invite/{invite_code}/join", Join);

        app.MapGet("api/invite/{invite_code}/planet/name", GetPlanetName);
        app.MapGet("api/invite/{invite_code}/planet/icon_url", GetPlanetIconUrl);
    }

    private static async Task CreateInvite(HttpContext ctx, ValourDB db, [FromHeader] string authorization)
    {
        var authToken = await ServerAuthToken.TryAuthorize(authorization, db);
        if (authToken == null) { await TokenInvalid(ctx); return; }

        ServerPlanetInvite in_invite = await JsonSerializer.DeserializeAsync<ServerPlanetInvite>(ctx.Request.Body);

        ServerPlanetMember member = await db.PlanetMembers.Include(x => x.Planet).FirstOrDefaultAsync(x => x.Planet_Id == in_invite.Planet_Id && x.User_Id == authToken.User_Id);

        if (member == null) { await Unauthorized("Member not found", ctx); return; }

        if (!await member.HasPermissionAsync(PlanetPermissions.Invite, db))
        {
            await Unauthorized("Member lacks PlanetPermissions.Invite", ctx);
            return;
        }

        // Ensure important fields are correct
        in_invite.Issuer_Id = authToken.User_Id;
        in_invite.Time = DateTime.UtcNow;
        in_invite.Id = IdManager.Generate();

        Random random = new();

        const string chars = "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789";
        string code = "";

        bool exists = false;

        do
        {
            code = new string(Enumerable.Repeat(chars, 8).Select(s => s[random.Next(s.Length)]).ToArray());
            exists = await db.PlanetInvites.AnyAsync(x => x.Code == code);
        }
        while (!exists);

        in_invite.Code = code;

        if (in_invite.Hours < 1)
        {
            in_invite.Hours = null;
        }

        await db.PlanetInvites.AddAsync(in_invite);
        await db.SaveChangesAsync();

        ctx.Response.StatusCode = 201;
        await ctx.Response.WriteAsync(in_invite.Code);
    }

    private static async Task GetPlanetName(HttpContext ctx, ValourDB db, string invite_code, [FromHeader] string authorization)
    {
        var authToken = await ServerAuthToken.TryAuthorize(authorization, db);
        if (authToken == null) { await TokenInvalid(ctx); return; }

        var invite = await db.PlanetInvites.Include(x => x.Planet).FirstOrDefaultAsync(x => x.Code == invite_code);

        if (invite is null) { await NotFound("Invite not found", ctx); return; }

        ctx.Response.StatusCode = 200;
        await ctx.Response.WriteAsync(invite.Planet.Name);
    }

    private static async Task GetPlanetIconUrl(HttpContext ctx, ValourDB db, string invite_code, [FromHeader] string authorization)
    {
        var authToken = await ServerAuthToken.TryAuthorize(authorization, db);
        if (authToken == null) { await TokenInvalid(ctx); return; }

        var invite = await db.PlanetInvites.Include(x => x.Planet).FirstOrDefaultAsync(x => x.Code == invite_code);

        if (invite is null) { await NotFound("Invite not found", ctx); return; }

        ctx.Response.StatusCode = 200;
        await ctx.Response.WriteAsync(invite.Planet.Image_Url);
    }

    private static async Task Join(HttpContext ctx, ValourDB db, string invite_code, [FromHeader] string authorization)
    {
        var authToken = await ServerAuthToken.TryAuthorize(authorization, db);
        if (authToken == null) { await TokenInvalid(ctx); return; }

        var invite = await db.PlanetInvites.Include(x => x.Planet).FirstOrDefaultAsync(x => x.Code == invite_code);
        if (invite == null) { await NotFound("Invite code not found", ctx); return; }

        if (await db.PlanetBans.AnyAsync(x => x.User_Id == authToken.User_Id && x.Planet_Id == invite.Planet_Id))
        {
            await BadRequest("User is banned from the planet", ctx);
            return;
        }

        if (await db.PlanetMembers.AnyAsync(x => x.User_Id == authToken.User_Id && x.Planet_Id == invite.Planet_Id))
        {
            await BadRequest("User is already a member", ctx);
            return;
        }

        if (!invite.Planet.Public)
        {
            await Unauthorized("Planet is set to private", ctx);
            return;
        }

        var user = await db.Users.FindAsync(authToken.User_Id);

        await invite.Planet.AddMemberAsync(user, db);

        ctx.Response.StatusCode = 200;
        await ctx.Response.WriteAsync("Success");
    }

    private static async Task GetInvite(HttpContext ctx, ValourDB db, string invite_code, [FromHeader] string authorization)
    {
        var authToken = await ServerAuthToken.TryAuthorize(authorization, db);
        if (authToken == null) { await TokenInvalid(ctx); return; }

        var invite = await db.PlanetInvites.Include(x => x.Planet).FirstOrDefaultAsync(x => x.Code == invite_code);
        if (invite == null) { await NotFound("Invite code not found", ctx); return; }

        if (!invite.IsPermanent())
        {
            if (DateTime.UtcNow > invite.Time.AddMinutes((double)(invite.Hours * 60))){
                db.PlanetInvites.Remove(invite);
                await db.SaveChangesAsync();
                ctx.Response.StatusCode = 200;
                await ctx.Response.WriteAsync("Invite is expired");
                return;
            }
        }

        var ban = await db.PlanetBans.FirstOrDefaultAsync(x => x.User_Id == authToken.User_Id && x.Planet_Id == invite.Planet_Id);
        if (ban is not null) { await Unauthorized("User is banned", ctx); return; }

        if (!invite.Planet.Public) { await Unauthorized("Planet is set to private", ctx); return; }

        ctx.Response.StatusCode = 200;
        await ctx.Response.WriteAsJsonAsync(invite);
    }

}
