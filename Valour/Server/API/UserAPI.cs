using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Valour.Server.Database;
using Valour.Server.Oauth;
using Valour.Server.Planets;
using Valour.Server.Users;
using Valour.Shared.Oauth;
using Valour.Shared.Planets;

namespace Valour.Server.API
{
    public class UserAPI : BaseAPI
    {
        public static void AddRoutes(WebApplication app)
        {
            app.MapGet("api/user/{user_id}/planets", GetPlanets);
        }
        
        private static async Task GetPlanets(HttpContext ctx, ValourDB db, ulong user_id,
                                            [FromHeader] string authorization)
        {
            AuthToken auth = await ServerAuthToken.TryAuthorize(authorization, db);

            if (auth == null)
            {
                ctx.Response.StatusCode = 401;
                await ctx.Response.WriteAsync($"Token is invalid [token: {authorization}]");
                return;
            }

            if (!auth.HasScope(UserPermissions.Membership))
            {
                ctx.Response.StatusCode = 401;
                await ctx.Response.WriteAsync($"Token lacks UserPermissions.Membership");
                return;
            }

            if (auth.User_Id != user_id)
            {
                ctx.Response.StatusCode = 401;
                await ctx.Response.WriteAsync($"User id does not match token holder");
                return;
            }

            ServerUser user = await db.Users
                .Include(x => x.Membership)
                .ThenInclude(x => x.Planet)
                .FirstOrDefaultAsync(x => x.Id == user_id);

            if (user == null)
            {
                ctx.Response.StatusCode = 404;
                await ctx.Response.WriteAsync($"User not found [id: {user_id.ToString()}]");
                return;
            }

            ctx.Response.StatusCode = 200;
            await ctx.Response.WriteAsJsonAsync(user.Membership.Select(x => x.Planet));
        }
    }
}