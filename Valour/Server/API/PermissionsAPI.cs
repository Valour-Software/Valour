
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Valour.Server.Database;
using Valour.Server.Oauth;
using Valour.Server.Planets;
using Valour.Server.Roles;
using Valour.Shared.Oauth;

namespace Valour.Server.API;
public class PermissionsAPI : BaseAPI
{
    public static void AddRoutes(WebApplication app)
    {
        app.MapGet("api/node/{target_id}/{role_id}", GetNode);

        app.MapGet("api/node/{node_id}", GetNodeById);

        app.MapPost("api/node", SetNode);
    }

    private static async Task GetNodeById(HttpContext ctx, ValourDB db, ulong node_id,
        [FromHeader] string authorization)
    {
        var authToken = await ServerAuthToken.TryAuthorize(authorization, db);
        if (authToken is null) { await TokenInvalid(ctx); return; }

        if (!authToken.HasScope(UserPermissions.Membership)) { await Unauthorized("Token lacks UserPermissions.Membership", ctx); return; }

        var node = await db.PermissionsNodes.FindAsync(node_id);

        if (node is null) { await NotFound("Node not found", ctx); return; }

        var member = await ServerPlanetMember.FindAsync(authToken.User_Id, node.Target_Id, db);

        if (member is null) { await Unauthorized("Member not found", ctx); return; }

        ctx.Response.StatusCode = 200;
        await ctx.Response.WriteAsJsonAsync(node);
    }

    private static async Task GetNode(HttpContext ctx, ValourDB db, ulong target_id, ulong role_id,
        [FromHeader] string authorization)
    {
        var authToken = await ServerAuthToken.TryAuthorize(authorization, db);
        if (authToken is null) { await TokenInvalid(ctx); return; }

        if (!authToken.HasScope(UserPermissions.Membership)) { await Unauthorized("Token lacks UserPermissions.Membership", ctx); return; }

        var node = await db.PermissionsNodes.FirstOrDefaultAsync(x => x.Target_Id == target_id && x.Role_Id == role_id);

        if (node is null) { await NotFound("Node not found", ctx); return; }

        var member = await ServerPlanetMember.FindAsync(authToken.User_Id, node.Planet_Id, db);

        if (member is null) { await Unauthorized("Member not found", ctx); return; }

        ctx.Response.StatusCode = 200;
        await ctx.Response.WriteAsJsonAsync(node);
    }

    private static async Task SetNode(HttpContext ctx, ValourDB db, [FromBody] PermissionsNode node,
        [FromHeader] string authorization)
    {
        var authToken = await ServerAuthToken.TryAuthorize(authorization, db);

        var member = await ServerPlanetMember.FindAsync(authToken.User_Id, node.Planet_Id, db);

        if (member is null) { await Unauthorized("Member not found", ctx); return; }

        if (!authToken.HasScope(UserPermissions.PlanetManagement)) { await Unauthorized("Token lacks UserPermissions.PlanetManagement", ctx); return; }

        var target = await node.GetTarget(db);

        if (target is null) { await NotFound("Node target not found", ctx); return; }

        if (await member.GetAuthorityAsync() < node.Role.GetAuthority())
        {
            await Unauthorized("Role has greater authority", ctx);
            return;
        }

        var planet = await target.get

        // Check global permission first
        if (!await target.Planet.HasPermissionAsync(member, PlanetPermissions.ManageRoles, db))
        {
            await Unauthorized("Member lacks PlanetPermissions.ManageRoles", ctx);
            return;
        }

        // Check for channel-specific perm
        if (!await channel.HasPermission(member, ChatChannelPermissions.ManagePermissions, db))
        {
            await Unauthorized("Member lacks ChatChannelPermissions.ManagePermissions", ctx);
            return;
        }

        var old = await db.ChatChannelPermissionsNodes.Include(x => x.Role).Include(x => x.Planet).Include(x => x.Channel).FirstOrDefaultAsync(x => x.Id == node.Id);

        if (old is not null)
        {
            if (old.Planet_Id != node.Planet_Id || old.Role_Id != node.Role_Id)
            {
                await BadRequest("Id mismatch", ctx);
                return;
            }

            // Update
            db.ChatChannelPermissionsNodes.Update(node);
            await db.SaveChangesAsync();

            ctx.Response.StatusCode = 200;
            await ctx.Response.WriteAsync("Success");
        }
        else
        {
            node.Id = IdManager.Generate();
            node.Planet_Id = channel.Planet_Id;

            await db.ChatChannelPermissionsNodes.AddAsync(node);
            await db.SaveChangesAsync();

            ctx.Response.StatusCode = 201;
            await ctx.Response.WriteAsync("Success");
        }
    }
}
