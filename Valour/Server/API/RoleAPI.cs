using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Valour.Server.Database;
using Valour.Server.Extensions;
using Valour.Server.Oauth;
using Valour.Server.Planets;
using Valour.Server.Roles;
using Valour.Shared.Oauth;
using Valour.Shared.Planets;
using Valour.Shared.Roles;

/*  Valour - A free and secure chat client
 *  Copyright (C) 2021 Vooper Media LLC
 *  This program is subject to the GNU Affero General Public license
 *  A copy of the license should be included - if not, see <http://www.gnu.org/licenses/>
 */

namespace Valour.Server.API
{
    public static class RoleAPI
    {
        /// <summary>
        /// Adds the routes for this API section
        /// </summary>
        public static void AddRoutes(WebApplication app)
        {
            app.Map("api/role/{role_id}", Role);
        }
        
        private static async Task Role(HttpContext ctx, ValourDB db, ulong role_id,
            [FromHeader] string authorization)
        {
            AuthToken auth = await ServerAuthToken.TryAuthorize(authorization, db);
            if (auth is null) { Results.Unauthorized(); return; }

            ServerPlanetRole role = await db.PlanetRoles.FindAsync(role_id);

            if (role == null)
            {
                ctx.Response.StatusCode = 404;
                await ctx.Response.WriteAsync($"Role not found");
                return;
            }

            ServerPlanetMember member = await db.PlanetMembers
                .Include(x => x.Planet)
                .FirstOrDefaultAsync(x => x.Planet_Id == role.Planet_Id &&
                x.User_Id == auth.User_Id);
            switch (ctx.Request.Method)
            {
                case "GET":
                {
                    if (member == null)
                    {
                        ctx.Response.StatusCode = 403;
                        await ctx.Response.WriteAsync($"Member not found");
                        return;
                    }
                    
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