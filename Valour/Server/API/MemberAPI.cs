using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Valour.Server.Database;
using Valour.Server.Oauth;
using Valour.Server.Planets;
using Valour.Shared.Oauth;

/*  Valour - A free and secure chat client
 *  Copyright (C) 2021 Vooper Media LLC
 *  This program is subject to the GNU Affero General Public license
 *  A copy of the license should be included - if not, see <http://www.gnu.org/licenses/>
 */

namespace Valour.Server.API
{
    public static class MemberAPI
    {
        /// <summary>
        /// Adds the routes for this API section
        /// </summary>
        public static void AddRoutes(WebApplication app)
        {
            app.MapGet("api/member/planet/{planet_id}/user/{user_id}", GetMemberWithIds);
            app.MapGet("api/member/planet/{planet_id}/user/{user_id}/role_ids", GetMemberRoleIds);
        }

        private static async Task GetMemberRoleIds(HttpContext ctx, ValourDB db, ulong planet_id, ulong user_id,
            [FromHeader] string authorization)
        {
            AuthToken auth = await ServerAuthToken.TryAuthorize(authorization, db);

            if (auth == null)
            {
                ctx.Response.StatusCode = 401;
                await ctx.Response.WriteAsync($"Token is invalid [token: {authorization}]");
                return;
            }

            ServerPlanet planet = await db.Planets.Include(x => x.Members.Where(x => x.User_Id == auth.User_Id))
                .FirstOrDefaultAsync(x => x.Id == planet_id);

            if (planet == null)
            {
                ctx.Response.StatusCode = 400;
                await ctx.Response.WriteAsync($"Planet not found [id: {planet_id.ToString()}]");
                return;
            }

            ServerPlanetMember member = planet.Members.FirstOrDefault();

            if (member == null)
            {
                ctx.Response.StatusCode = 401;
                await ctx.Response.WriteAsync($"Member not found");
                return;
            }

            if (!await planet.HasPermissionAsync(member, PlanetPermissions.View, db))
            {
                ctx.Response.StatusCode = 401;
                await ctx.Response.WriteAsync("Member lacks PlanetPermissions.View");
                return;
            }

            ServerPlanetMember target = await db.PlanetMembers.Include(x => x.RoleMembership.OrderBy(x => x.Role.Position))
                .ThenInclude(x => x.Role)
                .FirstOrDefaultAsync(x => x.Planet_Id == planet_id && x.User_Id == user_id);
            
            if (target == null)
            {
                ctx.Response.StatusCode = 400;
                await ctx.Response.WriteAsync($"Member not found [user_id: {user_id.ToString()}, planet_id: {planet_id.ToString()}");

                return;
            }

            ctx.Response.StatusCode = 200;
            await ctx.Response.WriteAsJsonAsync(target.RoleMembership.Select(x => x.Role_Id));
            return;
        }
        
        private static async Task GetMemberWithIds(HttpContext ctx, ValourDB db, ulong planet_id, ulong user_id,
            [FromHeader] string authorization)
        {
            AuthToken auth = await ServerAuthToken.TryAuthorize(authorization, db);

            if (auth == null)
            {
                ctx.Response.StatusCode = 401;
                await ctx.Response.WriteAsync($"Token is invalid [token: {authorization}]");
                return;
            }

            ServerPlanet planet = await db.Planets.Include(x => x.Members.Where(x => x.User_Id == auth.User_Id))
                .FirstOrDefaultAsync(x => x.Id == planet_id);

            if (planet == null)
            {
                ctx.Response.StatusCode = 400;
                await ctx.Response.WriteAsync($"Planet not found [id: {planet_id}]");
                return;
            }

            ServerPlanetMember member = planet.Members.FirstOrDefault();

            if (member == null)
            {
                ctx.Response.StatusCode = 401;
                await ctx.Response.WriteAsync($"Member not found");
                return;
            }

            if (!await planet.HasPermissionAsync(member, PlanetPermissions.View, db))
            {
                ctx.Response.StatusCode = 401;
                await ctx.Response.WriteAsync("Member lacks PlanetPermissions.View");
                return;
            }

            ServerPlanetMember target = await db.PlanetMembers.FirstOrDefaultAsync(x => x.Planet_Id == planet_id && x.User_Id == user_id);
            
            if (target == null)
            {
                ctx.Response.StatusCode = 400;
                await ctx.Response.WriteAsync($"Member not found [user_id: {user_id.ToString()}, planet_id: {planet_id.ToString()}");

                return;
            }

            ctx.Response.StatusCode = 200;
            await ctx.Response.WriteAsJsonAsync(target);
            return;
        }
        
    }
}