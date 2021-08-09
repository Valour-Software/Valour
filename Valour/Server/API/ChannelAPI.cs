using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using Valour.Server.Database;
using Valour.Server.Oauth;
using Valour.Server.Planets;
using Valour.Shared;
using Valour.Shared.Oauth;

namespace Valour.Server.API
{
    public class ChannelAPI
    {
        public static void AddRoutes(WebApplication app)
        {
            app.MapGet("/channel/delete", Delete);
        }

        /// <summary>
        /// Deletes the given channel
        /// </summary>
        // -----------------------------------
        // Type:
        // GET
        // -----------------------------------
        // Route:
        // /channel/delete
        // -----------------------------------
        // Query parameters:
        // auth       : Authentication key
        // channel_id : Id of target channel

        private static async Task Delete(HttpContext ctx, ValourDB db)
        {
            // Request parameter validation //
            
            if (!ctx.Request.Query.TryGetValue("token", out var token))
            {
                ctx.Response.StatusCode = 401;
                await ctx.Response.WriteAsync("Unauthorized. Include token.");
                return;
            }

            if (!ctx.Request.Query.TryGetValue("channel_id", out var channel_id_in))
            {
                ctx.Response.StatusCode = 400;
                await ctx.Response.WriteAsync("Include channe_id.");
                return;
            }

            ulong channel_id;
            bool channel_id_parse = ulong.TryParse(channel_id_in, out channel_id);

            if (!channel_id_parse)
            {
                ctx.Response.StatusCode = 400;
                await ctx.Response.WriteAsync("Could not parse channel_id.");
                return;
            }

            // Request authorization //

            ServerAuthToken authToken = await ServerAuthToken.TryAuthorize(token, db);

            if (authToken == null)
            {
                ctx.Response.StatusCode = 401;
                await ctx.Response.WriteAsync("Token is invalid.");
                return;
            }

            ServerPlanetChatChannel channel = await db.PlanetChatChannels.Include(x => x.Planet)
                                                                         .ThenInclude(x => x.Members.Where(x => x.User_Id == authToken.User_Id))
                                                                         .FirstOrDefaultAsync(x => x.Id == channel_id);

            if (channel == null)
            {
                ctx.Response.StatusCode = 400;
                await ctx.Response.WriteAsync($"Could not find channel with [id: {channel_id}]");
                return;
            }

            // We should have ONLY loaded in the target member
            ServerPlanetMember member = channel.Planet.Members.FirstOrDefault();

            if (!await channel.Planet.HasPermissionAsync(member, PlanetPermissions.ManageChannels, db))
            {
                ctx.Response.StatusCode = 401;
                await ctx.Response.WriteAsync($"Could not authorize member for [node: ManageChannels]");
                return;
            }

            if (channel_id == channel.Planet.Main_Channel_Id)
            {
                ctx.Response.StatusCode = 400;
                await ctx.Response.WriteAsync($"You cannot delete a planet's main channel [id: {channel_id}]");
                return;
            }

            // Request action //

            // Remove permission nodes
            db.ChatChannelPermissionsNodes.RemoveRange(
                db.ChatChannelPermissionsNodes.Where(x => x.Channel_Id == channel_id)
            );

            // Remove messages
            db.PlanetMessages.RemoveRange(
                db.PlanetMessages.Where(x => x.Channel_Id == channel_id)
            );

            // Remove channel
            db.PlanetChatChannels.Remove(channel);

            // Save changes
            await db.SaveChangesAsync();

            // Notify channel deletion
            await PlanetHub.NotifyChatChannelDeletion(channel);

            ctx.Response.StatusCode = 200;
            await ctx.Response.WriteAsync($"Removed channel [id: {channel_id}]");
        }
    }
}
