
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
        app.MapGet("api/node/channel/{channel_id}/{role_id}", GetChannelNode);
        app.MapGet("api/node/category/{category_id}/{role_id}", GetCategoryNode);

        app.MapPost("api/node/channel", SetChannelNode);
        app.MapPost("api/node/category", SetCategoryNode);
    }

    private static async Task GetChannelNode(HttpContext ctx, ValourDB db, ulong channel_id, ulong role_id,
        [FromHeader] string authorization)
    {
        var authToken = await ServerAuthToken.TryAuthorize(authorization, db);
        if (authToken is null) { await TokenInvalid(ctx); return; }

        if (!authToken.HasScope(UserPermissions.Membership)) { await Unauthorized("Token lacks UserPermissions.Membership", ctx); return; }

        var node = await db.ChatChannelPermissionsNodes.FirstOrDefaultAsync(x => x.Channel_Id == channel_id && x.Role_Id == role_id);

        if (node is null) { await NotFound("Node not found", ctx); return; }

        var member = await ServerPlanetMember.FindAsync(authToken.User_Id, node.Planet_Id, db);

        if (member is null) { await Unauthorized("Member not found", ctx); return; }

        ctx.Response.StatusCode = 200;
        await ctx.Response.WriteAsJsonAsync(node);
    }

    private static async Task GetCategoryNode(HttpContext ctx, ValourDB db, ulong category_id, ulong role_id,
        [FromHeader] string authorization)
    {
        var authToken = await ServerAuthToken.TryAuthorize(authorization, db);
        if (authToken is null) { await TokenInvalid(ctx); return; }

        if (!authToken.HasScope(UserPermissions.Membership)) { await Unauthorized("Token lacks UserPermissions.Membership", ctx); return; }

        var node = await db.CategoryPermissionsNodes.FirstOrDefaultAsync(x => x.Category_Id == category_id && x.Role_Id == role_id);

        if (node is null) { await NotFound("Node not found", ctx); return; }

        var member = await ServerPlanetMember.FindAsync(authToken.User_Id, node.Planet_Id, db);

        if (member is null) { await Unauthorized("Member not found", ctx); return; }

        ctx.Response.StatusCode = 200;
        await ctx.Response.WriteAsJsonAsync(node);
    }

    private static async Task SetChannelNode(HttpContext ctx, ValourDB db, [FromBody] ServerChatChannelPermissionsNode node,
        [FromHeader] string authorization)
    {
        var authToken = await ServerAuthToken.TryAuthorize(authorization, db);

        var member = await ServerPlanetMember.FindAsync(authToken.User_Id, node.Planet_Id, db);

        if (member is null) { await Unauthorized("Member not found", ctx); return; }

        if (!authToken.HasScope(UserPermissions.PlanetManagement)) { await Unauthorized("Token lacks UserPermissions.PlanetManagement", ctx); return; }

        var channel = await db.PlanetChatChannels.Include(x => x.Planet).FirstOrDefaultAsync(x => x.Id == node.Channel_Id);

        if (channel is null) { await NotFound("Channel not found", ctx); return; }


        if (await member.GetAuthorityAsync() < node.Role.GetAuthority())
        {
            await Unauthorized("Role has greater authority", ctx);
            return;
        }

        // Check global permission first
        if (!await channel.Planet.HasPermissionAsync(member, PlanetPermissions.ManageRoles, db))
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

    private static async Task SetCategoryNode(HttpContext ctx, ValourDB db, [FromBody] ServerCategoryPermissionsNode node,
        [FromHeader] string authorization)
    {
        var authToken = await ServerAuthToken.TryAuthorize(authorization, db);

        var member = await ServerPlanetMember.FindAsync(authToken.User_Id, node.Planet_Id, db);

        if (member is null) { await Unauthorized("Member not found", ctx); return; }

        if (!authToken.HasScope(UserPermissions.PlanetManagement)) { await Unauthorized("Token lacks UserPermissions.PlanetManagement", ctx); return; }

        var category = await db.PlanetCategories.Include(x => x.Planet).FirstOrDefaultAsync(x => x.Id == node.Category_Id);

        if (category is null) { await NotFound("Category not found", ctx); return; }


        if (await member.GetAuthorityAsync() < node.Role.GetAuthority())
        {
            await Unauthorized("Role has greater authority", ctx);
            return;
        }

        // Check global permission first
        if (!await category.Planet.HasPermissionAsync(member, PlanetPermissions.ManageRoles, db))
        {
            await Unauthorized("Member lacks PlanetPermissions.ManageRoles", ctx);
            return;
        }

        // Check for channel-specific perm
        if (!await category.HasPermission(member, CategoryPermissions.ManagePermissions, db))
        {
            await Unauthorized("Member lacks ChatChannelPermissions.ManagePermissions", ctx);
            return;
        }

        var old = await db.CategoryPermissionsNodes.Include(x => x.Role).Include(x => x.Planet).Include(x => x.Category).FirstOrDefaultAsync(x => x.Id == node.Id);

        if (old is not null)
        {
            if (old.Planet_Id != node.Planet_Id || old.Role_Id != node.Role_Id)
            {
                await BadRequest("Id mismatch", ctx);
                return;
            }

            // Update
            db.CategoryPermissionsNodes.Update(node);
            await db.SaveChangesAsync();

            ctx.Response.StatusCode = 200;
            await ctx.Response.WriteAsync("Success");
        }
        else
        {
            node.Id = IdManager.Generate();
            node.Planet_Id = category.Planet_Id;

            await db.CategoryPermissionsNodes.AddAsync(node);
            await db.SaveChangesAsync();

            ctx.Response.StatusCode = 201;
            await ctx.Response.WriteAsync("Success");
        }
    }
}
