using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Valour.Server.Database;
using Valour.Server.Oauth;
using Valour.Server.Planets;
using Valour.Server.Roles;
using Valour.Shared;
using Valour.Shared.Oauth;
using Valour.Shared.Roles;

/*  Valour - A free and secure chat client
 *  Copyright (C) 2021 Vooper Media LLC
 *  This program is subject to the GNU Affero General Public license
 *  A copy of the license should be included - if not, see <http://www.gnu.org/licenses/>
 */

namespace Valour.Server.API
{
    public class MemberAPI : BaseAPI
    {
        /// <summary>
        /// Adds the routes for this API section
        /// </summary>
        public static void AddRoutes(WebApplication app)
        {
            app.MapGet   ("api/member/{member_id}", GetMember);
            app.MapDelete("api/member/{member_id}", DeleteMember);

            app.MapGet   ("api/member/{member_id}/role_membership", GetRoleMembership);
            app.MapPost  ("api/member/{member_id}/role_membership", AddRoleMembership);
            app.MapDelete("api/member/{member_id}/role_membership/{role_member_id}", RemoveRoleMembership);

            app.MapGet   ("api/member/{member_id}/roles", GetRoles);
            app.MapGet   ("api/member/{member_id}/role_ids", GetRoleIds);
            app.MapGet   ("api/member/{member_id}/authority", GetAuthority);
            
            app.MapDelete("api/member/{member_id}/roles/{role_id}", RemoveRole);

            app.MapGet("api/member/{member_id}/primary_role", PrimaryRole);

            app.MapGet("api/member/{planet_id}/{user_id}", GetMemberWithIds);
            app.MapGet("api/member/{planet_id}/{user_id}/role_ids", GetMemberRoleIds);
        }

        private static async Task GetMember(HttpContext ctx, ValourDB db, ulong member_id,
            [FromHeader] string authorization)
        {
            AuthToken authToken = await ServerAuthToken.TryAuthorize(authorization, db);
            if (authToken is null) { await TokenInvalid(ctx); return; }

            var targetMember = await ServerPlanetMember.FindAsync(member_id, db);

            if (targetMember is null) { await NotFound("Target member not found", ctx); return; }

            if (!await db.PlanetMembers.AnyAsync(x => x.User_Id == authToken.User_Id && x.Planet_Id == targetMember.Planet_Id))
            {
                await Unauthorized("Auth member not found", ctx); 
                return;
            }

            ctx.Response.StatusCode = 200;
            await ctx.Response.WriteAsJsonAsync(targetMember);
        }

        private static async Task DeleteMember(HttpContext ctx, ValourDB db, ulong member_id,
            [FromHeader] string authorization)
        {
            AuthToken authToken = await ServerAuthToken.TryAuthorize(authorization, db);
            if (authToken is null) { await TokenInvalid(ctx); return; }

            if (!authToken.HasScope(UserPermissions.PlanetManagement)) { await Unauthorized("Token lacks UserPermissions.PlanetManagement", ctx); return; }

            var targetMember = await ServerPlanetMember.FindAsync(member_id, db);

            if (targetMember is null) { await NotFound("Target member not found", ctx); return; }

            var authMember = await db.PlanetMembers.Include(x => x.Planet).FirstOrDefaultAsync(x => x.User_Id == authToken.User_Id && x.Planet_Id == targetMember.Planet_Id);

            if (authMember is null)
            {
                await Unauthorized("Auth member not found", ctx);
                return;
            }

            if (!await authMember.HasPermissionAsync(PlanetPermissions.Kick, db)) { await Unauthorized("Member lacks PlanetPermissions.Kick", ctx); return; }

            db.PlanetMembers.Remove(targetMember);
            await db.SaveChangesAsync();

            ctx.Response.StatusCode = 200;
            await ctx.Response.WriteAsync("Success");
        }

        private static async Task GetAuthority(HttpContext ctx, ValourDB db, ulong member_id,
            [FromHeader] string authorization)
        {
            AuthToken auth = await ServerAuthToken.TryAuthorize(authorization, db);
            if (auth is null) { await TokenInvalid(ctx); return; }

            ServerPlanetMember target_member = await db.PlanetMembers
                .Include(x => x.RoleMembership.OrderBy(x => x.Role.Position))
                .ThenInclude(x => x.Role)
                .FirstOrDefaultAsync(x => x.Id == member_id);


            if (target_member is null) { await NotFound("Target member not found", ctx); return; }

            // Ensure auth user is member of planet
            ServerPlanetMember auth_member = await db.PlanetMembers.FirstOrDefaultAsync(x => x.Planet_Id == target_member.Planet_Id &&
                                                                                        x.User_Id == auth.User_Id);

            if (auth_member is null) { await Unauthorized("Auth member not found", ctx); return; }

            ctx.Response.StatusCode = 200;
            await ctx.Response.WriteAsJsonAsync(await target_member.GetAuthorityAsync());
            return;
        }

        private static async Task PrimaryRole(HttpContext ctx, ValourDB db, ulong member_id,
            [FromHeader] string authorization)
        {
            AuthToken auth = await ServerAuthToken.TryAuthorize(authorization, db);
            if (auth is null) { await TokenInvalid(ctx); return; }

            ServerPlanetMember target_member = await db.PlanetMembers
                .Include(x => x.RoleMembership.OrderBy(x => x.Role.Position))
                .ThenInclude(x => x.Role)
                .FirstOrDefaultAsync(x => x.Id == member_id);


            if (target_member is null)
            {
                ctx.Response.StatusCode = 404;
                await ctx.Response.WriteAsync("Target member not found");
                return;
            }

            // Ensure auth user is member of planet
            ServerPlanetMember member = await db.PlanetMembers.FirstOrDefaultAsync(x => x.Planet_Id == target_member.Planet_Id &&
                                                                                        x.User_Id == auth.User_Id);

            if (member is null)
            {
                ctx.Response.StatusCode = 403;
                await ctx.Response.WriteAsync("Auth member not found");
                return;
            }

            if (member.Planet_Id != target_member.Planet_Id)
            {
                ctx.Response.StatusCode = 400;
                await ctx.Response.WriteAsync("Planet id mismatch");
                return;
            }

            ctx.Response.StatusCode = 200;
            await ctx.Response.WriteAsJsonAsync(target_member.RoleMembership.First().Role);
            return;
        }

        private static async Task RemoveRole(HttpContext ctx, ValourDB db, ulong member_id, ulong role_id,
            [FromHeader] string authorization)
        {
            AuthToken auth = await ServerAuthToken.TryAuthorize(authorization, db);
            if (auth is null) { await TokenInvalid(ctx); return; }

            ServerPlanetMember target_member = await db.PlanetMembers
                .Include(x => x.Planet)
                .Include(x => x.RoleMembership.OrderBy(x => x.Role.Position))
                .ThenInclude(x => x.Role)
                .FirstOrDefaultAsync(x => x.Id == member_id);


            if (target_member is null)
            {
                ctx.Response.StatusCode = 404;
                await ctx.Response.WriteAsync("Target member not found");
                return;
            }

            // Ensure auth user is member of planet
            ServerPlanetMember member = await db.PlanetMembers.FirstOrDefaultAsync(x => x.Planet_Id == target_member.Planet_Id &&
                                                                                        x.User_Id == auth.User_Id);

            if (member is null)
            {
                ctx.Response.StatusCode = 403;
                await ctx.Response.WriteAsync("Auth member not found");
                return;
            }

            if (!auth.HasScope(UserPermissions.PlanetManagement))
            {
                ctx.Response.StatusCode = 403;
                await ctx.Response.WriteAsync("Token lacks UserPermissions.PlanetManagement");
                return;
            }

            var role = await db.PlanetRoles.FindAsync(role_id);

            if (role is null)
            {
                ctx.Response.StatusCode = 400;
                await ctx.Response.WriteAsync("Role not found");
                return;
            }

            if (role.Planet_Id != member.Planet_Id || role.Planet_Id != target_member.Planet_Id)
            {
                ctx.Response.StatusCode = 400;
                await ctx.Response.WriteAsync("Planet id mismatch");
                return;
            }

            var callerAuth = await member.GetAuthorityAsync();

            // Ensure role has less authority than user adding it
            if (role.GetAuthority() >= callerAuth)
            {
                ctx.Response.StatusCode = 400;
                await ctx.Response.WriteAsync("Can only add or remove with lower authority than your own");
                return;
            }

            // Ensure target member has less authority than caller
            if (target_member.User_Id != auth.User_Id && await target_member.GetAuthorityAsync() >= callerAuth)
            {
                ctx.Response.StatusCode = 403;
                await ctx.Response.WriteAsync("Target has higher or equal authority");
                return;
            }

            var roleMember = await db.PlanetRoleMembers.FirstOrDefaultAsync(x => x.Member_Id == target_member.Id && x.Role_Id == role.Id);

            if (roleMember is null)
            {
                ctx.Response.StatusCode = 200;
                await ctx.Response.WriteAsync("Member already lacks role");
                return;
            }
            else
            {
                db.Remove(roleMember);
                await db.SaveChangesAsync();
            }

            ctx.Response.StatusCode = 200;
            await ctx.Response.WriteAsync("Success");

            return;
        }

        private static async Task RemoveRoleMembership(HttpContext ctx, ValourDB db, ulong member_id, ulong role_member_id,
            [FromHeader] string authorization)
        {
            AuthToken auth = await ServerAuthToken.TryAuthorize(authorization, db);
            if (auth is null) { await TokenInvalid(ctx); return; }

            ServerPlanetMember target_member = await db.PlanetMembers
                .Include(x => x.Planet)
                .Include(x => x.RoleMembership.OrderBy(x => x.Role.Position))
                .ThenInclude(x => x.Role)
                .FirstOrDefaultAsync(x => x.Id == member_id);


            if (target_member is null)
            {
                ctx.Response.StatusCode = 404;
                await ctx.Response.WriteAsync("Target member not found");
                return;
            }

            // Ensure auth user is member of planet
            ServerPlanetMember member = await db.PlanetMembers.FirstOrDefaultAsync(x => x.Planet_Id == target_member.Planet_Id &&
                                                                                        x.User_Id == auth.User_Id);

            if (member is null)
            {
                ctx.Response.StatusCode = 403;
                await ctx.Response.WriteAsync("Auth member not found");
                return;
            }

            if (!auth.HasScope(UserPermissions.PlanetManagement))
            {
                ctx.Response.StatusCode = 403;
                await ctx.Response.WriteAsync("Token lacks UserPermissions.PlanetManagement");
                return;
            }

            var roleMember = await db.PlanetRoleMembers.Include(x => x.Role).FirstOrDefaultAsync(x => x.Id == role_member_id);

            if (roleMember is null)
            {
                ctx.Response.StatusCode = 200;
                await ctx.Response.WriteAsync("Member already lacks role");
                return;
            }

            if (roleMember.Id != target_member.Id)
            {
                ctx.Response.StatusCode = 400;
                await ctx.Response.WriteAsync("Target id mismatch");
                return;
            }

            if (roleMember.Planet_Id != member.Planet_Id || roleMember.Planet_Id != target_member.Planet_Id)
            {
                ctx.Response.StatusCode = 400;
                await ctx.Response.WriteAsync("Planet id mismatch");
                return;
            }

            var callerAuth = await member.GetAuthorityAsync();

            // Ensure role has less authority than user adding it
            if (roleMember.Role.GetAuthority() >= callerAuth)
            {
                ctx.Response.StatusCode = 400;
                await ctx.Response.WriteAsync("Can only add remove with lower authority than your own");
                return;
            }

            // Ensure target member has less authority than caller
            if (target_member.User_Id != auth.User_Id && await target_member.GetAuthorityAsync() >= callerAuth)
            {
                ctx.Response.StatusCode = 403;
                await ctx.Response.WriteAsync("Target has higher or equal authority");
                return;
            }

            db.Remove(roleMember);
            await db.SaveChangesAsync();

            ctx.Response.StatusCode = 200;
            await ctx.Response.WriteAsync("Success");

            return;
        }

        private static async Task GetRoleIds(HttpContext ctx, ValourDB db, ulong member_id,
            [FromHeader] string authorization)
        {
            var authToken = await ServerAuthToken.TryAuthorize(authorization, db);
            if (authToken is null) { await TokenInvalid(ctx); return; }

            if (!authToken.HasScope(UserPermissions.Membership)) { await Unauthorized("Token lacks UserPermissions.Membership", ctx); return; }

            var targetMember = await db.PlanetMembers
                .Include(x => x.RoleMembership.OrderBy(x => x.Role.Position))
                .ThenInclude(x => x.Role)
                .FirstOrDefaultAsync(x => x.Id == member_id);

            if (targetMember is null) { await NotFound("Target member not found", ctx); return; }

            if (!await db.PlanetMembers.AnyAsync(x => x.Planet_Id == targetMember.Planet_Id && x.User_Id == authToken.User_Id))
            {
                await Unauthorized("Member not found", ctx);
                return;
            }

            ctx.Response.StatusCode = 200;
            await ctx.Response.WriteAsJsonAsync(targetMember.RoleMembership.Select(x => x.Role_Id)); 
        }

        private static async Task GetRoles(HttpContext ctx, ValourDB db, ulong member_id,
            [FromHeader] string authorization)
        {
            AuthToken auth = await ServerAuthToken.TryAuthorize(authorization, db);
            if (auth is null) { await TokenInvalid(ctx); return; }

            if (!auth.HasScope(UserPermissions.Membership)) { await Unauthorized("Token lacks UserPermissions.Membership", ctx); return; }

            ServerPlanetMember target_member = await db.PlanetMembers
                .Include(x => x.Planet)
                .Include(x => x.RoleMembership.OrderBy(x => x.Role.Position))
                .ThenInclude(x => x.Role)
                .FirstOrDefaultAsync(x => x.Id == member_id);


            if (target_member is null) { await NotFound("Target member not found", ctx); return; }

            // Ensure auth user is member of planet
            var member = await db.PlanetMembers.FirstOrDefaultAsync();

            if (!await db.PlanetMembers.AnyAsync(x => x.Planet_Id == target_member.Planet_Id && x.User_Id == auth.User_Id))
            {
                await Unauthorized("Auth member not found", ctx);
                return;
            }

            ctx.Response.StatusCode = 200;
            await ctx.Response.WriteAsJsonAsync(target_member.RoleMembership.Select(x => x.Role));
        }

        private static async Task GetRoleMembership(HttpContext ctx, ValourDB db, ulong member_id,
            [FromHeader] string authorization)
        {
            AuthToken authToken = await ServerAuthToken.TryAuthorize(authorization, db);
            if (authToken is null) { await TokenInvalid(ctx); return; }

            if (!authToken.HasScope(UserPermissions.Membership)) { await Unauthorized("Token lacks UserPermissions.Membership", ctx); return; }

            ServerPlanetMember target_member = await db.PlanetMembers
                .Include(x => x.Planet)
                .Include(x => x.RoleMembership.OrderBy(x => x.Role.Position))
                .ThenInclude(x => x.Role)
                .FirstOrDefaultAsync(x => x.Id == member_id);

            if (target_member is null) { await NotFound("Target member not found", ctx); return; }

            var authMember = await ServerPlanetMember.FindAsync(authToken.User_Id, target_member.Planet_Id, db);

            if (authMember is null) { await Unauthorized("Auth member not found", ctx); return; }

            ctx.Response.StatusCode = 200;
            await ctx.Response.WriteAsJsonAsync(target_member.RoleMembership);
        }

        private static async Task AddRoleMembership(HttpContext ctx, ValourDB db, ulong member_id,
            [FromHeader] string authorization)
        {
            AuthToken authToken = await ServerAuthToken.TryAuthorize(authorization, db);
            if (authToken is null) { await TokenInvalid(ctx); return; }

            if (!authToken.HasScope(UserPermissions.Membership)) { await Unauthorized("Token lacks UserPermissions.Membership", ctx); return; }
            if (!authToken.HasScope(UserPermissions.PlanetManagement)) { await Unauthorized("Token lacks UserPermissions.PlanetManagement", ctx); return; }

            ServerPlanetMember target_member = await db.PlanetMembers
                .Include(x => x.Planet)
                .FirstOrDefaultAsync(x => x.Id == member_id);

            if (target_member is null) { await NotFound("Target member not found", ctx); return; }

            var authMember = await ServerPlanetMember.FindAsync(authToken.User_Id, target_member.Planet_Id, db);

            if (authMember is null) { await Unauthorized("Auth member not found", ctx); return; }

            if (!await authMember.HasPermissionAsync(PlanetPermissions.ManageRoles, db)) { await Unauthorized("Member lacks PlanetPermissions.ManageRoles", ctx); return; }

            var roleMember = await JsonSerializer.DeserializeAsync<ServerPlanetRoleMember>(ctx.Request.Body);

            if (await db.PlanetRoleMembers.AnyAsync(x => x.Member_Id == member_id && x.Role_Id == roleMember.Role_Id))
            {
                await BadRequest("User already has role", ctx);
                return;
            }

            var role = await db.PlanetRoles.FindAsync(roleMember.Role_Id);

            if (role is null) { await NotFound("Role not found", ctx); return; }

            if (roleMember.Member_Id != target_member.Id || roleMember.User_Id != target_member.User_Id
                || roleMember.Planet_Id != target_member.Planet_Id)
            {
                await BadRequest("Id mismatch", ctx);
                return;
            }

            var authAuthority = await authMember.GetAuthorityAsync();

            if (role.GetAuthority() >= authAuthority) { await Unauthorized("The role has greater authority than you", ctx); return; }

            if (await target_member.GetAuthorityAsync() > authAuthority) { await Unauthorized("The target member has greater authority than you", ctx); return; }

            // Add id
            roleMember.Id = IdManager.Generate();

            await db.PlanetRoleMembers.AddAsync(roleMember);
            await db.SaveChangesAsync();

            ctx.Response.StatusCode = 201;
            await ctx.Response.WriteAsync(roleMember.Id.ToString());
            return;

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