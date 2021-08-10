using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using Valour.Server.Categories;
using Valour.Server.Database;
using Valour.Server.MPS;
using Valour.Server.Oauth;
using Valour.Server.Planets;
using Valour.Server.Workers;
using Valour.Shared;
using Valour.Shared.Messages;
using Valour.Shared.Oauth;

/*  Valour - A free and secure chat client
 *  Copyright (C) 2021 Vooper Media LLC
 *  This program is subject to the GNU Affero General Public license
 *  A copy of the license should be included - if not, see <http://www.gnu.org/licenses/>
 */

namespace Valour.Server.API
{
    public class ChannelAPI
    {
        public static void AddRoutes(WebApplication app)
        {
            app.MapPost("/channel/delete", Delete);
            app.MapPost("/channel/setparent", SetParent);
            app.MapPost("/channel/create", Create);
            app.MapPost("/channel/postmessage", PostMessage);

            app.MapGet("/channel/getmessages", GetMessages);
            app.MapGet("/channel/getlastmessages", GetLastMessages);
        }

        /// <summary>
        /// Attempts to post a message to the channel
        /// </summary>

        // Type:
        // POST
        // -----------------------------------
        //
        // Route:
        // /channel/postmessage
        // -----------------------------------
        //
        // Query parameters:
        // ----------------------------------------
        // | token | Authentication key | string  |
        // ----------------------------------------
        //
        // POST Content:
        // --------------------------------------------------
        // | message | The message to post | PlanetMessage  |
        // --------------------------------------------------
        private static async Task PostMessage(HttpContext ctx, ValourDB db,
                                              [FromBody][Required] PlanetMessage message, [Required] string token)
        {


            AuthToken auth = await ServerAuthToken.TryAuthorize(token, db);

            if (auth == null)
            {
                ctx.Response.StatusCode = 401;
                await ctx.Response.WriteAsync($"Token is invalid [token: {token}]");
                return;
            }

            if (message == null)
            {
                ctx.Response.StatusCode = 400;
                await ctx.Response.WriteAsync($"Include message data");
                return;
            }

            ServerPlanetChatChannel channel = await db.PlanetChatChannels.Include(x => x.Planet)
                                                                         .ThenInclude(x => x.Members.Where(x => x.User_Id == auth.User_Id))
                                                                         .FirstOrDefaultAsync(x => x.Id == message.Channel_Id);

            if (channel == null)
            {
                ctx.Response.StatusCode = 400;
                await ctx.Response.WriteAsync($"Channel not found [id: {message.Channel_Id}]");
                return;
            }

            var member = channel.Planet.Members.FirstOrDefault();

            if (member == null)
            {
                ctx.Response.StatusCode = 401;
                await ctx.Response.WriteAsync("Could not find member using token");
                return;
            }

            if (!await channel.HasPermission(member, ChatChannelPermissions.ViewMessages, db))
            {
                ctx.Response.StatusCode = 401;
                await ctx.Response.WriteAsync("Member lacks ChatChannelPermissions.ViewMessages node");
                return;
            }

            if (!await channel.HasPermission(member, ChatChannelPermissions.PostMessages, db))
            {
                ctx.Response.StatusCode = 401;
                await ctx.Response.WriteAsync("Member lacks ChatChannelPermissions.PostMessages node");
                return;
            }

            // Ensure author id is accurate
            message.Author_Id = auth.User_Id;

            if (message.Content != null && message.Content.Length > 2048)
            {
                ctx.Response.StatusCode = 400;
                await ctx.Response.WriteAsync("Content is over 2048 chars");
                return;
            }

            if (message.Embed_Data != null && message.Content.Length > 65535)
            {
                ctx.Response.StatusCode = 400;
                await ctx.Response.WriteAsync("Embed is over 65535 chars");
                return;
            }

            // Handle urls
            message.Content = await MPSManager.HandleUrls(message.Content);

            PlanetMessageWorker.AddToQueue(message);

            StatWorker.IncreaseMessageCount();

            ctx.Response.StatusCode = 200;
            await ctx.Response.WriteAsync("Success");
        }


        /// <summary>
        /// Returns the last messages from a channel
        /// </summary>

        // Type:
        // GET
        // -----------------------------------
        //
        // Route:
        // /channel/getlastmessages
        // -----------------------------------
        //
        // Query parameters:
        // -----------------------------------------------------------
        // | token      | Authentication key               | string  |
        // | channel_id | Id of the channel                | ulong   |
        // | count      | The amount of messages to return | int     |
        // -----------------------------------------------------------
        private static async Task GetLastMessages(HttpContext ctx, ValourDB db,
                                                  [Required] string token, [Required] ulong channel_id, int count = 10)
        {
            // Request parameter validation //

            if (count > 64)
            {
                ctx.Response.StatusCode = 400;
                await ctx.Response.WriteAsync("Max count is 64");
                return;
            }

            // Request authorization //

            AuthToken auth = await ServerAuthToken.TryAuthorize(token, db);

            if (auth == null)
            {
                ctx.Response.StatusCode = 401;
                await ctx.Response.WriteAsync($"Token is invalid [token: {token}]");
                return;
            }

            ServerPlanetChatChannel channel = await db.PlanetChatChannels.Include(x => x.Planet)
                                                                         .ThenInclude(x => x.Members.Where(x => x.User_Id == auth.User_Id))
                                                                         .FirstOrDefaultAsync(x => x.Id == channel_id);

            var member = channel.Planet.Members.FirstOrDefault();

            if (member == null || !await channel.HasPermission(member, ChatChannelPermissions.ViewMessages, db))
            {
                ctx.Response.StatusCode = 401;
                await ctx.Response.WriteAsync("Member lacks ChatChannelPermissions.ViewMessages node");
                return;
            }

            List<PlanetMessage> staged = PlanetMessageWorker.GetStagedMessages(channel_id, count);
            List<PlanetMessage> messages = null;

            count = count - staged.Count;

            if (count > 0)
            {
                await Task.Run(() =>
                {
                    messages =
                    db.PlanetMessages.Where(x => x.Channel_Id == channel_id)
                                     .OrderByDescending(x => x.Message_Index)
                                     .Take(count)
                                     .Reverse()
                                     .ToList();
                });

                messages.AddRange(staged);
            }

            ctx.Response.StatusCode = 200;
            await ctx.Response.WriteAsJsonAsync(messages);
        }

        /// <summary>
        /// Returns the messages from a channel
        /// </summary>

        // Type:
        // GET
        // -----------------------------------
        //
        // Route:
        // /channel/getmessages
        // -----------------------------------
        //
        // Query parameters:
        // -----------------------------------------------------------
        // | token      | Authentication key               | string  |
        // | channel_id | Id of the channel                | ulong   |
        // | index      | The index at which to start      | ulong   |
        // | count      | The amount of messages to return | int     |
        // -----------------------------------------------------------
        private static async Task GetMessages(HttpContext ctx, ValourDB db)
        {
            // Request parameter validation //

            if (!ctx.Request.Query.TryGetValue("token", out var token))
            {
                ctx.Response.StatusCode = 401;
                await ctx.Response.WriteAsync("Include token");
                return;
            }

            if (!ctx.Request.Query.TryGetValue("channel_id", out var channel_id_in))
            {
                ctx.Response.StatusCode = 400;
                await ctx.Response.WriteAsync("Include channel_id");
                return;
            }

            ulong channel_id;
            bool channel_id_parse = ulong.TryParse(channel_id_in, out channel_id);

            if (!channel_id_parse)
            {
                ctx.Response.StatusCode = 400;
                await ctx.Response.WriteAsync("Could not parse channel_id");
                return;
            }

            ulong index;

            if (!ctx.Request.Query.TryGetValue("index", out var index_in))
            {
                index = ulong.MaxValue;
            }
            else
            {
                bool index_parse = ulong.TryParse(index_in, out index);

                if (!index_parse)
                {
                    ctx.Response.StatusCode = 400;
                    await ctx.Response.WriteAsync("Could not parse index");
                    return;
                }
            }

            int count;

            if (!ctx.Request.Query.TryGetValue("count", out var count_in))
            {
                count = 10;
            }
            else
            {
                bool count_parse = int.TryParse(count_in, out count);

                if (!count_parse)
                {
                    ctx.Response.StatusCode = 400;
                    await ctx.Response.WriteAsync("Could not parse count");
                    return;
                }

                if (count > 64)
                {
                    ctx.Response.StatusCode = 400;
                    await ctx.Response.WriteAsync("Max count is 64");
                    return;
                }
            }

            // Request authorization //

            AuthToken auth = await ServerAuthToken.TryAuthorize(token, db);

            if (auth == null)
            {
                ctx.Response.StatusCode = 401;
                await ctx.Response.WriteAsync($"Token is invalid [token: {token}]");
                return;
            }

            ServerPlanetChatChannel channel = await db.PlanetChatChannels.Include(x => x.Planet)
                                                                         .ThenInclude(x => x.Members.Where(x => x.User_Id == auth.User_Id))
                                                                         .FirstOrDefaultAsync(x => x.Id == channel_id);

            var member = channel.Planet.Members.FirstOrDefault();

            if (member == null || !await channel.HasPermission(member, ChatChannelPermissions.ViewMessages, db))
            {
                ctx.Response.StatusCode = 401;
                await ctx.Response.WriteAsync("Member lacks ChatChannelPermissions.ViewMessages node");
                return;
            }

            List<PlanetMessage> staged = PlanetMessageWorker.GetStagedMessages(channel_id, count);
            List<PlanetMessage> messages = null;

            count = count - staged.Count;

            if (count > 0)
            {
                await Task.Run(() =>
                {
                    messages =
                    db.PlanetMessages.Where(x => x.Channel_Id == channel_id && x.Message_Index < index)
                                     .OrderByDescending(x => x.Message_Index)
                                     .Take(count)
                                     .Reverse()
                                     .ToList();
                });

                messages.AddRange(staged.Where(x => x.Message_Index < index));
            }

            ctx.Response.StatusCode = 200;
            await ctx.Response.WriteAsJsonAsync(messages);
        }

        /// <summary>
        /// Creates a channel
        /// </summary>

        // Type:
        // POST
        // -----------------------------------
        //
        // Route:
        // /channel/create
        // -----------------------------------
        //
        // Query parameters:
        // ---------------------------------------------------
        // | token     | Authentication key        | string  |
        // | planet_id | Id of target planet       | ulong   |
        // | parent_id | Id of the parent category | ulong   |
        // | name      | The name for the channel  | string  |
        // ---------------------------------------------------
        private static async Task Create(HttpContext ctx, ValourDB db)
        {
            // Request parameter validation //

            if (!ctx.Request.Query.TryGetValue("token", out var token))
            {
                ctx.Response.StatusCode = 401;
                await ctx.Response.WriteAsync("Include token");
                return;
            }

            if (!ctx.Request.Query.TryGetValue("planet_id", out var planet_id_in))
            {
                ctx.Response.StatusCode = 400;
                await ctx.Response.WriteAsync("Include planet_id");
                return;
            }

            if (!ctx.Request.Query.TryGetValue("parent_id", out var parent_id_in))
            {
                ctx.Response.StatusCode = 400;
                await ctx.Response.WriteAsync("Include channel_id");
                return;
            }

            if (!ctx.Request.Query.TryGetValue("name", out var name))
            {
                ctx.Response.StatusCode = 401;
                await ctx.Response.WriteAsync("Include name");
                return;
            }

            ulong planet_id;
            bool planet_id_parse = ulong.TryParse(planet_id_in, out planet_id);

            if (!planet_id_parse)
            {
                ctx.Response.StatusCode = 400;
                await ctx.Response.WriteAsync("Could not parse planet_id");
                return;
            }

            ulong parent_id;
            bool parent_id_parse = ulong.TryParse(parent_id_in, out parent_id);

            if (!parent_id_parse)
            {
                ctx.Response.StatusCode = 400;
                await ctx.Response.WriteAsync("Could not parse parent_id");
                return;
            }

            TaskResult name_valid = ServerPlanetChatChannel.ValidateName(name);

            if (!name_valid.Success)
            {
                ctx.Response.StatusCode = 400;
                await ctx.Response.WriteAsync($"Name is not valid [name: {name}]");
                return;
            }

            // Request authorization //

            AuthToken auth = await ServerAuthToken.TryAuthorize(token, db);

            if (!auth.HasScope(UserPermissions.PlanetManagement))
            {
                ctx.Response.StatusCode = 401;
                await ctx.Response.WriteAsync("Token lacks UserPermissions.PlanetManagement scope");
                return;
            }

            ServerPlanet planet = await db.Planets.Include(x => x.Members.Where(x => x.User_Id == auth.User_Id))
                                                  .FirstOrDefaultAsync(x => x.Id == planet_id);

            var member = planet.Members.FirstOrDefault();

            if (!await planet.HasPermissionAsync(member, PlanetPermissions.ManageChannels, db))
            {
                ctx.Response.StatusCode = 401;
                await ctx.Response.WriteAsync("Member lacks PlanetPermissions.ManageChannels node");
                return;
            }

            // Request action //

            // Creates the channel

            ServerPlanetChatChannel channel = new ServerPlanetChatChannel()
            {
                Id = IdManager.Generate(),
                Name = name,
                Planet_Id = planet_id,
                Parent_Id = parent_id,
                Message_Count = 0,
                Description = "A chat channel",
                Position = (ushort)(await db.PlanetChatChannels.CountAsync(x => x.Parent_Id == parent_id))
            };

            // Add channel to database
            await db.PlanetChatChannels.AddAsync(channel);

            // Save changes to DB
            await db.SaveChangesAsync();

            // Send channel refresh
            PlanetHub.NotifyChatChannelChange(channel);

            ctx.Response.StatusCode = 200;
            await ctx.Response.WriteAsync(channel.Id.ToString());
        }

        /// <summary>
        /// Sets the parent of the given channel
        /// </summary>

        // Type:
        // POST
        // -----------------------------------
        // Route:
        // /channel/setparent
        //
        // -----------------------------------
        //
        // Query parameters:
        // --------------------------------------------------
        // | token      | Authentication key       | string |
        // | channel_id | Id of target channel     | ulong  |
        // | parent_id  | Id of the parent channel | ulong  |
        // --------------------------------------------------

        private static async Task SetParent(HttpContext ctx, ValourDB db)
        {
            // Request parameter validation //

            if (!ctx.Request.Query.TryGetValue("token", out var token))
            {
                ctx.Response.StatusCode = 401;
                await ctx.Response.WriteAsync("Include token");
                return;
            }

            if (!ctx.Request.Query.TryGetValue("channel_id", out var channel_id_in))
            {
                ctx.Response.StatusCode = 400;
                await ctx.Response.WriteAsync("Include channel_id");
                return;
            }

            if (!ctx.Request.Query.TryGetValue("parent_id", out var parent_id_in))
            {
                ctx.Response.StatusCode = 400;
                await ctx.Response.WriteAsync("Include channel_id");
                return;
            }

            ulong channel_id;
            bool channel_id_parse = ulong.TryParse(channel_id_in, out channel_id);

            if (!channel_id_parse)
            {
                ctx.Response.StatusCode = 400;
                await ctx.Response.WriteAsync("Could not parse channel_id");
                return;
            }

            ulong parent_id;
            bool parent_id_parse = ulong.TryParse(parent_id_in, out parent_id);

            if (!parent_id_parse)
            {
                ctx.Response.StatusCode = 400;
                await ctx.Response.WriteAsync("Could not parse parent_id");
                return;
            }

            // Request authorization //

            AuthToken auth = await ServerAuthToken.TryAuthorize(token, db);

            if (!auth.HasScope(UserPermissions.PlanetManagement))
            {
                ctx.Response.StatusCode = 401;
                await ctx.Response.WriteAsync("Token lacks UserPermissions.PlanetManagement scope");
                return;
            }

            ServerPlanetChatChannel channel = await db.PlanetChatChannels.Include(x => x.Planet)
                                                                         .ThenInclude(x => x.Members.Where(x => x.User_Id == auth.User_Id))
                                                                         .FirstOrDefaultAsync(x => x.Id == channel_id);

            if (channel == null)
            {
                ctx.Response.StatusCode = 400;
                await ctx.Response.WriteAsync($"Could not find channel [id: {channel_id}]");
                return;
            }

            ServerPlanetMember member = channel.Planet.Members.FirstOrDefault();

            if (!await channel.Planet.HasPermissionAsync(member, PlanetPermissions.ManageChannels, db))
            {
                ctx.Response.StatusCode = 401;
                await ctx.Response.WriteAsync($"Member lacks PlanetPermissions.ManageChannels");
                return;
            }

            // Request action //

            // If parent does not exist or does not belong to the same planet
            if (!await db.PlanetCategories.AnyAsync(x => x.Id == parent_id && x.Planet_Id == channel.Planet_Id))
            {
                ctx.Response.StatusCode = 400;
                await ctx.Response.WriteAsync($"Parent does not exist or belongs to another planet [id: {parent_id}]");
                return;
            }

            // Fulfill request
            channel.Parent_Id = parent_id;

            await db.SaveChangesAsync();

            // Notify of change
            PlanetHub.NotifyChatChannelChange(channel);

            ctx.Response.StatusCode = 200;
            await ctx.Response.WriteAsync("Success");
        }

        /// <summary>
        /// Deletes the given channel
        /// </summary>

        // Type:
        // POST
        // -----------------------------------
        //
        // Route:
        // /channel/delete
        // -----------------------------------
        //
        // Query parameters:
        // ----------------------------------------------
        // | token      | Authentication key   | string |
        // | channel_id | Id of target channel | ulong  |
        // ----------------------------------------------

        private static async Task Delete(HttpContext ctx, ValourDB db)
        {
            // Request parameter validation //
            
            if (!ctx.Request.Query.TryGetValue("token", out var token))
            {
                ctx.Response.StatusCode = 401;
                await ctx.Response.WriteAsync("Include token");
                return;
            }

            if (!ctx.Request.Query.TryGetValue("channel_id", out var channel_id_in))
            {
                ctx.Response.StatusCode = 400;
                await ctx.Response.WriteAsync("Include channel_id");
                return;
            }

            ulong channel_id;
            bool channel_id_parse = ulong.TryParse(channel_id_in, out channel_id);

            if (!channel_id_parse)
            {
                ctx.Response.StatusCode = 400;
                await ctx.Response.WriteAsync($"Could not parse channel_id");
                return;
            }

            // Request authorization //

            ServerAuthToken auth = await ServerAuthToken.TryAuthorize(token, db);

            if (auth == null)
            {
                ctx.Response.StatusCode = 401;
                await ctx.Response.WriteAsync($"Token is invalid [token: {token}]");
                return;
            }

            ServerPlanetChatChannel channel = await db.PlanetChatChannels.Include(x => x.Planet)
                                                                         .ThenInclude(x => x.Members.Where(x => x.User_Id == auth.User_Id))
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
