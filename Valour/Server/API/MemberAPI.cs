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
            app.Map      ("api/member/{member_id}", Member);
            app.Map      ("api/member/{member_id}/role_membership", RoleMembership);
            app.Map      ("api/member/{member_id}/roles", Roles);
            app.MapGet   ("api/member/{member_id}/authority", GetAuthority);
            app.MapDelete("api/member/{member_id}/role_membership/{role_member_id}", RemoveRoleMembership);
            app.MapDelete("api/member/{member_id}/roles/{role_id}", RemoveRole);

            app.MapGet("api/member/{member_id}/primary_role", PrimaryRole);

            app.MapGet("api/member/planet/{planet_id}/user/{user_id}", GetMemberWithIds);
            app.MapGet("api/member/planet/{planet_id}/user/{user_id}/role_ids", GetMemberRoleIds);
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
                await ctx.Response.WriteAsync("Can only add remove with lower authority than your own");
                return;
            }

            // Ensure target member has less authority than caller
            if (await target_member.GetAuthorityAsync() >= callerAuth)
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
            if (await target_member.GetAuthorityAsync() >= callerAuth)
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


        private static async Task Roles(HttpContext ctx, ValourDB db, ulong member_id,
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

            switch (ctx.Request.Method)
            {
                case "GET":
                    {
                        ctx.Response.StatusCode = 200;
                        await ctx.Response.WriteAsJsonAsync(target_member.RoleMembership.Select(x => x.Role));
                        return;
                    }
                case "POST":
                    {
                        if (!auth.HasScope(UserPermissions.PlanetManagement))
                        {
                            ctx.Response.StatusCode = 403;
                            await ctx.Response.WriteAsync("Token lacks UserPermissions.PlanetManagement");
                            return;
                        }

                        if (!await member.HasPermissionAsync(PlanetPermissions.ManageRoles, db))
                        {
                            ctx.Response.StatusCode = 403;
                            await ctx.Response.WriteAsync("Token lacks PlanetPermissions.ManageRoles");
                            return;
                        }

                        // Get body role object
                        var inrole = await JsonSerializer.DeserializeAsync<ServerPlanetRole>(ctx.Request.Body);
                        var role = await db.PlanetRoles.FindAsync(inrole.Id);

                        if (role is null)
                        {
                            ctx.Response.StatusCode = 400;
                            await ctx.Response.WriteAsync("Include role in body");
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
                            ctx.Response.StatusCode = 403;
                            await ctx.Response.WriteAsync("Can only add roles with lower authority than your own");
                            return;
                        }

                        // Ensure target member has less authority than caller
                        if (await target_member.GetAuthorityAsync() >= callerAuth)
                        {
                            ctx.Response.StatusCode = 403;
                            await ctx.Response.WriteAsync("Target has higher or equal authority");
                            return;
                        }

                        ServerPlanetRoleMember roleMember = new()
                        {
                            Id = IdManager.Generate(),
                            Member_Id = target_member.Id,
                            User_Id = target_member.User_Id,
                            Planet_Id = target_member.Planet_Id,
                            Role_Id = role.Id
                        };

                        await db.PlanetRoleMembers.AddAsync(roleMember);
                        await db.SaveChangesAsync();

                        ctx.Response.StatusCode = 201;
                        await ctx.Response.WriteAsync(roleMember.Id.ToString());
                        return;

                    }
            }
        }

        private static async Task RoleMembership(HttpContext ctx, ValourDB db, ulong member_id,
            [FromHeader] string authorization)
        {
            AuthToken auth = await ServerAuthToken.TryAuthorize(authorization, db);
            if (auth is null) { await TokenInvalid(ctx); return; }

            ServerPlanetMember target_member = await db.PlanetMembers
                .Include(x => x.Planet)
                .Include(x => x.RoleMembership)
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

            switch (ctx.Request.Method)
            {
                case "GET":
                    {
                        ctx.Response.StatusCode = 200;
                        await ctx.Response.WriteAsJsonAsync(target_member.RoleMembership);
                        return;
                    }
                case "POST":
                    {
                        if (!auth.HasScope(UserPermissions.PlanetManagement))
                        {
                            ctx.Response.StatusCode = 403;
                            await ctx.Response.WriteAsync("Token lacks UserPermissions.PlanetManagement");
                            return;
                        }

                        if (!await member.HasPermissionAsync(PlanetPermissions.ManageRoles, db))
                        {
                            ctx.Response.StatusCode = 403;
                            await ctx.Response.WriteAsync("Token lacks PlanetPermissions.ManageRoles");
                            return;
                        }

                        // Get body rolemember object
                        var roleMember = await JsonSerializer.DeserializeAsync<ServerPlanetRoleMember>(ctx.Request.Body);

                        if (roleMember is null)
                        {
                            ctx.Response.StatusCode = 400;
                            await ctx.Response.WriteAsync("Include role member in body");
                            return;
                        }

                        PlanetRole role = await db.PlanetRoles.FindAsync(roleMember.Role_Id);

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
                            ctx.Response.StatusCode = 403;
                            await ctx.Response.WriteAsync("Can only add roles with lower authority than your own");
                            return;
                        }

                        // Ensure target member has less authority than caller
                        if (await target_member.GetAuthorityAsync() >= callerAuth)
                        {
                            ctx.Response.StatusCode = 403;
                            await ctx.Response.WriteAsync("Target has higher or equal authority");
                            return;
                        }

                        // Ensure things match properly
                        roleMember.Member_Id = target_member.Id;
                        roleMember.User_Id = target_member.User_Id;
                        roleMember.Planet_Id = target_member.Planet_Id;

                        // Add id
                        roleMember.Id = IdManager.Generate();

                        await db.PlanetRoleMembers.AddAsync(roleMember);
                        await db.SaveChangesAsync();

                        ctx.Response.StatusCode = 201;
                        await ctx.Response.WriteAsync(roleMember.Id.ToString());
                        return;

                    }
            }
        }

        private static async Task Member(HttpContext ctx, ValourDB db, ulong member_id,
            [FromHeader] string authorization)
        {
            AuthToken auth = await ServerAuthToken.TryAuthorize(authorization, db);
            if (auth is null) { await TokenInvalid(ctx); return; }

            ServerPlanetMember target_member = await db.PlanetMembers
                .Include(x => x.Planet)
                .FirstOrDefaultAsync(x => x.Id == member_id);

            if (target_member == null)
            {
                ctx.Response.StatusCode = 403;
                await ctx.Response.WriteAsync("Target not found");
                return;
            }

            var member = await db.PlanetMembers.FirstOrDefaultAsync(x => x.Planet_Id == target_member.Id &&
                x.User_Id == auth.User_Id);

            if (member == null)
            {
                ctx.Response.StatusCode = 401;
                await ctx.Response.WriteAsync($"Auth member not found");
                return;
            }

            switch (ctx.Request.Method)
            {
                case "GET":
                    {
                        ctx.Response.StatusCode = 200;
                        await ctx.Response.WriteAsJsonAsync(target_member);
                        return;
                    }
                case "DELETE":
                    {
                        if (!auth.HasScope(UserPermissions.PlanetManagement))
                        {
                            ctx.Response.StatusCode = 401;
                            await ctx.Response.WriteAsync("Token lacks UserPermissions.PlanetManagement");
                            return;
                        }

                        var result = await target_member.Planet.TryKickMemberAsync(member, target_member, db);

                        ctx.Response.StatusCode = result.Data;
                        await ctx.Response.WriteAsync(result.Message);

                        return;
                    }
            }
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