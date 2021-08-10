using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Threading.Tasks;
using Valour.Server.Database;
using Valour.Server.Oauth;
using Valour.Server.Planets;
using Valour.Shared.Oauth;

namespace Valour.Server.API
{
    public class PlanetAPI
    {
        public static void AddRoutes(WebApplication app)
        {
            app.MapGet("/planet/getchannelids", GetChannelIds);
            app.MapGet("/planet/getchannels", GetChannels);
        }

        /// <summary>
        /// Returns the currently visible channels in a planet
        /// </summary>

        // Type:
        // GET
        // -----------------------------------
        //
        // Route:
        // /planet/getchannels
        // -----------------------------------
        //
        // Query parameters:
        // --------------------------------------------
        // | token      | Authentication key | string |
        // | planet_id  | Id of the planet   | ulong  |
        // --------------------------------------------
        private static async Task GetChannels(HttpContext ctx, ValourDB db,
                                                [Required] string token, [Required] ulong planet_id)
        {
            ServerAuthToken auth = await ServerAuthToken.TryAuthorize(token, db);

            if (auth == null)
            {
                ctx.Response.StatusCode = 401;
                await ctx.Response.WriteAsync($"Token is invalid [token: {token}]");
                return;
            }

            ServerPlanet planet = await db.Planets.Include(x => x.Members.Where(x => x.User_Id == auth.User_Id))
                                                  .Include(x => x.ChatChannels)
                                                  .FirstOrDefaultAsync(x => x.Id == planet_id);

            if (planet == null)
            {
                ctx.Response.StatusCode = 400;
                await ctx.Response.WriteAsync($"Planet not found [id: {planet_id}]");
                return;
            }

            var member = planet.Members.FirstOrDefault();

            if (member == null)
            {
                ctx.Response.StatusCode = 401;
                await ctx.Response.WriteAsync($"Member not found");
                return;
            }

            List<ServerPlanetChatChannel> result = new List<ServerPlanetChatChannel>();

            foreach (var channel in planet.ChatChannels)
            {
                if (await channel.HasPermission(member, ChatChannelPermissions.View, db))
                {
                    result.Add(channel);
                }
            }

            ctx.Response.StatusCode = 200;
            await ctx.Response.WriteAsJsonAsync(result);
        }

        /// <summary>
        /// Returns the Ids of currently visible channels in a planet
        /// </summary>

        // Type:
        // GET
        // -----------------------------------
        //
        // Route:
        // /planet/getchannelids
        // -----------------------------------
        //
        // Query parameters:
        // --------------------------------------------
        // | token      | Authentication key | string |
        // | planet_id  | Id of the planet   | ulong  |
        // --------------------------------------------
        private static async Task GetChannelIds(HttpContext ctx, ValourDB db,
                                                [Required] string token, [Required] ulong planet_id)
        {
            ServerAuthToken auth = await ServerAuthToken.TryAuthorize(token, db);

            if (auth == null)
            {
                ctx.Response.StatusCode = 401;
                await ctx.Response.WriteAsync($"Token is invalid [token: {token}]");
                return;
            }

            ServerPlanet planet = await db.Planets.Include(x => x.Members.Where(x => x.User_Id == auth.User_Id))
                                                  .Include(x => x.ChatChannels)
                                                  .FirstOrDefaultAsync(x => x.Id == planet_id);

            if (planet == null)
            {
                ctx.Response.StatusCode = 400;
                await ctx.Response.WriteAsync($"Planet not found [id: {planet_id}]");
                return;
            }

            var member = planet.Members.FirstOrDefault();

            if (member == null)
            {
                ctx.Response.StatusCode = 401;
                await ctx.Response.WriteAsync($"Member not found");
                return;
            }

            List<ulong> result = new List<ulong>();

            foreach (var channel in planet.ChatChannels)
            {
                if (await channel.HasPermission(member, ChatChannelPermissions.View, db))
                {
                    result.Add(channel.Id);
                }
            }

            ctx.Response.StatusCode = 200;
            await ctx.Response.WriteAsJsonAsync(result);
        }
    }
}
