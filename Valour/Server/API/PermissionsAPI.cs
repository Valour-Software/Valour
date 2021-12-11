
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Valour.Database;
using Valour.Database.Items.Authorization;
using Valour.Database.Items.Planets.Members;
using Valour.Shared.Authorization;

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
        var authToken = await AuthToken.TryAuthorize(authorization, db);
        if (authToken is null) { await TokenInvalid(ctx); return; }

        if (!authToken.HasScope(UserPermissions.Membership)) { await Unauthorized("Token lacks UserPermissions.Membership", ctx); return; }

        var node = await db.PermissionsNodes.FindAsync(node_id);

        // A node not existing is fine
        if (node is null) 
        {  
            ctx.Response.StatusCode = 404;
            await ctx.Response.WriteAsync("null");
            return; 
        }

        var member = await PlanetMember.FindAsync(authToken.User_Id, node.Target_Id, db);

        if (member is null) { await Unauthorized("Member not found", ctx); return; }

        ctx.Response.StatusCode = 200;
        await ctx.Response.WriteAsJsonAsync(node);
    }

    private static async Task GetNode(HttpContext ctx, ValourDB db, ulong target_id, ulong role_id,
        [FromHeader] string authorization)
    {
        var authToken = await AuthToken.TryAuthorize(authorization, db);
        if (authToken is null) { await TokenInvalid(ctx); return; }

        if (!authToken.HasScope(UserPermissions.Membership)) { await Unauthorized("Token lacks UserPermissions.Membership", ctx); return; }

        var node = await db.PermissionsNodes.FirstOrDefaultAsync(x => x.Target_Id == target_id && x.Role_Id == role_id);

        // A node not existing is fine
        if (node is null) 
        {  
            ctx.Response.StatusCode = 404;
            await ctx.Response.WriteAsync("null");
            return; 
        }

        var member = await PlanetMember.FindAsync(authToken.User_Id, node.Planet_Id, db);

        if (member is null) { await Unauthorized("Member not found", ctx); return; }

        ctx.Response.StatusCode = 200;
        await ctx.Response.WriteAsJsonAsync(node);
    }

    private static async Task SetNode(HttpContext ctx, ValourDB db, [FromBody] PermissionsNode node,
        [FromHeader] string authorization)
    {
        // Start Authorization //

        var authToken = await AuthToken.TryAuthorize(authorization, db);

        var member = await PlanetMember.FindAsync(authToken.User_Id, node.Planet_Id, db);

        if (member is null) { await Unauthorized("Member not found", ctx); return; }

        if (!authToken.HasScope(UserPermissions.PlanetManagement)) { await Unauthorized("Token lacks UserPermissions.PlanetManagement", ctx); return; }

        // End Start Authorization //

        // Get role //

        PlanetRole role = null;

        var oldNode = await db.PermissionsNodes
            .Include(x => x.Role)
            .ThenInclude(x => x.Planet)
            .FirstOrDefaultAsync(x => x.Id == node.Id);

        if (oldNode is null){
            role = await db.PlanetRoles
                .Include(x => x.Planet)
                .FirstOrDefaultAsync(x => x.Id == node.Role_Id);
        }
        else{
            role = oldNode.Role;
            node.Role_Id = role.Id;

            db.Entry(oldNode).State = EntityState.Detached;
        }

        if (role is null){
            await NotFound("Role not found", ctx); 
            return;
        }

        // Get planet from role
        var planet = role.Planet;
        member.Planet = planet;
        node.Planet = planet;

        // End role stuff //
        
        var target = await node.GetTargetAsync(db);

        target.Planet = planet;

        if (target is null) { await NotFound("Node target not found", ctx); return; }

        if (target.Planet_Id != planet.Id){
            await BadRequest("Target does not belong to node's planet!", ctx);
            return;
        }

        if (await member.GetAuthorityAsync() < role.GetAuthority())
        {
            await Unauthorized("Role has greater authority", ctx);
            return;
        }

        if (!await member.HasPermissionAsync(PlanetPermissions.ManageRoles, db)){
            await Unauthorized("Member lacks PlanetPermissions.ManageRoles", ctx);
            return;
        }

        // Check for channel-specific perm
        if (!await target.HasPermission(member, ChatChannelPermissions.ManagePermissions, db))
        {
            await Unauthorized("Member lacks ChatChannelPermissions.ManagePermissions", ctx);
            return;
        }

        if (oldNode is not null)
        {
            // Update
            db.PermissionsNodes.Update(node);
            await db.SaveChangesAsync();

            ctx.Response.StatusCode = 200;
            await ctx.Response.WriteAsync("Success");
        }
        else
        {
            node.Id = IdManager.Generate();
            node.Planet_Id = target.Planet_Id;

            await db.PermissionsNodes.AddAsync(node);
            await db.SaveChangesAsync();

            ctx.Response.StatusCode = 201;
            await ctx.Response.WriteAsync("Success");
        }
    }
}
