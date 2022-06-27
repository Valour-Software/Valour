using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using Valour.Database;
using Valour.Database.Items.Authorization;
using Valour.Database.Items.Planets;
using Valour.Database.Items.Planets.Channels;
using Valour.Database.Items.Planets.Members;
using Valour.Database.Items.Users;
using Valour.Server.Extensions;
using Valour.Server.Planets.Members;
using Valour.Shared;
using Valour.Shared.Authorization;

/*  Valour - A free and secure chat client
 *  Copyright (C) 2021 Vooper Media LLC
 *  This program is subject to the GNU Affero General Public license
 *  A copy of the license should be included - if not, see <http://www.gnu.org/licenses/>
 */

namespace Valour.Server.API
{
    public class PlanetAPI : BaseAPI
    {


        /// <summary>
        /// Adds the routes for this API section
        /// </summary>
        public static void AddRoutes(WebApplication app)
        {
            // Planet routes //
            app.MapPost("api/planet/{planet_id}/roles/order", SetRoleOrder);

            app.MapGet("api/planet/{planet_id}/invites", GetInvites);

            // Planet member routes //

            app.MapPost("api/planet/{planet_id}/members/{target_id}/kick", KickMember);
            app.MapPost("api/planet/{planet_id}/members/{target_id}/ban", BanMember);
        }

        private static async Task GetInvites(HttpContext ctx, ValourDB db, ulong planet_id,
            [FromHeader] string authorization)
        {
            var authToken = await AuthToken.TryAuthorize(authorization, db);
            if (authToken == null) { await TokenInvalid(ctx); return; }

            PlanetMember member = await db.PlanetMembers
                .Include(x => x.Planet)
                .ThenInclude(x => x.Invites)
                .FirstOrDefaultAsync(x => x.Planet_Id == planet_id && x.User_Id == authToken.User_Id);

            if (member == null) { await Unauthorized("Member not found", ctx); return; }

            if (!await member.HasPermissionAsync(PlanetPermissions.Invite, db)) { await Unauthorized("Member lacks PlanetPermissions.Invite", ctx); return; }

            ctx.Response.StatusCode = 200;
            await ctx.Response.WriteAsJsonAsync(member.Planet.Invites);
        }


    }
}