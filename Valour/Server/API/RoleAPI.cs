using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Valour.Database;
using Valour.Database.Items.Authorization;
using Valour.Database.Items.Planets;
using Valour.Shared.Oauth;


/*  Valour - A free and secure chat client
 *  Copyright (C) 2021 Vooper Media LLC
 *  This program is subject to the GNU Affero General Public license
 *  A copy of the license should be included - if not, see <http://www.gnu.org/licenses/>
 */

namespace Valour.Server.API
{
    public class RoleAPI : BaseAPI
    {
        /// <summary>
        /// Adds the routes for this API section
        /// </summary>
        public static void AddRoutes(WebApplication app)
        {
            app.Map("api/role/{role_id}", Role);
            app.MapPost("api/role", AddRole);
        }

        private static async Task AddRole(HttpContext ctx, ValourDB db,
            [FromHeader] string authorization)
        {
            AuthToken auth = await ServerAuthToken.TryAuthorize(authorization, db);
            if (auth is null) { await TokenInvalid(ctx); return; }

            ServerPlanetRole in_role = await JsonSerializer.DeserializeAsync<ServerPlanetRole>(ctx.Response.Body);

            ServerPlanetMember member = await db.PlanetMembers.FirstOrDefaultAsync(x => x.User_Id == auth.User_Id &&
                                                                                        x.Planet_Id == in_role.Planet_Id);

            if (member is null) { await Unauthorized("Member not found", ctx); return; }
            if (!auth.HasScope(UserPermissions.PlanetManagement)) { await Unauthorized("Auth token lacks UserPermissions.PlanetManagement", ctx); return; }
            if (!await member.HasPermissionAsync(PlanetPermissions.ManageRoles, db)) { await Unauthorized("Member lacks PlanetPermissions.ManageRoles", ctx); return; }


            // Ensure fields are correct
            in_role.Planet_Id = member.Planet_Id;
            in_role.Position = (uint)await db.PlanetRoles.CountAsync(x => x.Planet_Id == in_role.Planet_Id);

            // Generate ID
            in_role.Id = IdManager.Generate();

            await db.PlanetRoles.AddAsync(in_role);
            await db.SaveChangesAsync();

            ctx.Response.StatusCode = 201;
            await ctx.Response.WriteAsJsonAsync(in_role.Id);
            return;
        }
        
        private static async Task Role(HttpContext ctx, ValourDB db, ulong role_id,
            [FromHeader] string authorization)
        {
            AuthToken auth = await ServerAuthToken.TryAuthorize(authorization, db);
            if (auth is null) { await TokenInvalid(ctx); return; }

            ServerPlanetRole role = await db.PlanetRoles.FindAsync(role_id);

            if (role == null) { await NotFound("Role not found.", ctx); return; }

            ServerPlanetMember member = await db.PlanetMembers
                .Include(x => x.Planet)
                .FirstOrDefaultAsync(x => x.Planet_Id == role.Planet_Id &&
                x.User_Id == auth.User_Id);

            if (member == null) { await NotFound("Member not found", ctx); return; }

            switch (ctx.Request.Method)
            {
                case "GET":
                {                    
                    ctx.Response.StatusCode = 200;
                    await ctx.Response.WriteAsJsonAsync(role);
                    return;
                }
                case "DELETE":
                {
                    var result = await role.TryDeleteAsync(member, db);

                    ctx.Response.StatusCode = result.Data;
                    await ctx.Response.WriteAsync(result.Message);
                    return;
                }
                case "PUT":
                {
                    ServerPlanetRole in_role = await JsonSerializer.DeserializeAsync<ServerPlanetRole>(ctx.Response.Body);

                    var result = await role.TryUpdateAsync(member, in_role, db);
                    
                    ctx.Response.StatusCode = result.Data;
                    await ctx.Response.WriteAsync(result.Message);
                    
                    return;
                }
            }
        }
    }
}