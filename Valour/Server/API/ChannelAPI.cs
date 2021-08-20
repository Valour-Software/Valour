using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Valour.Server.Database;
using Valour.Server.Extensions;
using Valour.Server.MPS;
using Valour.Server.Oauth;
using Valour.Server.Planets;
using Valour.Server.Workers;
using Valour.Shared;
using Valour.Shared.Items;
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
            app.MapGet("/api/channel/{channel_id}/messages", GetMessages);
            app.MapPost("/api/channel/{channel_id}/messages", PostMessage);

            app.Map("/api/channel/{channel_id}", Channel);
            app.Map("/api/channel/{channel_id}/name", Name);
            app.Map("/api/channel/{channel_id}/parent_id", ParentId);
            app.Map("/api/channel/{channel_id}/description", Description);
            app.Map("/api/channel/{channel_id}/inherits_perms", PermissionsInherit);
        }



        private static async Task Channel(HttpContext ctx, ValourDB db, ulong channel_id,
                                         [FromHeader] string authorization)
        {
            AuthToken auth = await ServerAuthToken.TryAuthorize(authorization, db);

            if (auth == null)
            {
                ctx.Response.StatusCode = 401;
                await ctx.Response.WriteAsync($"Token is invalid [token: {authorization}]");
                return;
            }

            ServerPlanetChatChannel channel = await db.PlanetChatChannels.Include(x => x.Planet)
                                                                         .ThenInclude(x => x.Members.Where(x => x.User_Id == auth.User_Id))
                                                                         .FirstOrDefaultAsync(x => x.Id == channel_id);

            if (channel == null)
            {
                ctx.Response.StatusCode = 400;
                await ctx.Response.WriteAsync($"Channel not found [id: {channel_id}]");
                return;
            }

            var member = channel.Planet.Members.FirstOrDefault();

            if (member == null)
            {
                ctx.Response.StatusCode = 401;
                await ctx.Response.WriteAsync("Member not found");
                return;
            }

            if (!await channel.HasPermission(member, ChatChannelPermissions.View, db))
            {
                ctx.Response.StatusCode = 401;
                await ctx.Response.WriteAsync("Member lacks ChatChannelPermissions.View");
                return;
            }

            switch (ctx.Request.Method)
            {
                case "GET":
                    {
                        ctx.Response.StatusCode = 200;
                        await ctx.Response.WriteAsJsonAsync((IPlanetChatChannel)channel);
                        return;

                    }
                case "DELETE":
                    {
                        if (!auth.HasScope(UserPermissions.PlanetManagement))
                        {
                            ctx.Response.StatusCode = 401;
                            await ctx.Response.WriteAsync($"Token lacks UserPermissions.PlanetManagement");
                            return;
                        }

                        if (!await channel.HasPermission(member, ChatChannelPermissions.ManageChannel, db))
                        {
                            ctx.Response.StatusCode = 401;
                            await ctx.Response.WriteAsync("Member lacks ChatChannelPermissions.ManageChannel");
                            return;
                        }

                        TaskResult result = await channel.TryDeleteAsync(db);

                        if (!result.Success)
                        {
                            ctx.Response.StatusCode = 400;
                        }
                        else
                        {
                            ctx.Response.StatusCode = 200;
                        }

                        await ctx.Response.WriteAsync(result.Message);
                        return;
                    }
            }
        }

        private static async Task PermissionsInherit(HttpContext ctx, ValourDB db, ulong channel_id,
                                         [FromHeader] string authorization)
        {
            AuthToken auth = await ServerAuthToken.TryAuthorize(authorization, db);

            if (auth == null)
            {
                ctx.Response.StatusCode = 401;
                await ctx.Response.WriteAsync($"Token is invalid [token: {authorization}]");
                return;
            }

            ServerPlanetChatChannel channel = await db.PlanetChatChannels.Include(x => x.Planet)
                                                                         .ThenInclude(x => x.Members.Where(x => x.User_Id == auth.User_Id))
                                                                         .FirstOrDefaultAsync(x => x.Id == channel_id);

            if (channel == null)
            {
                ctx.Response.StatusCode = 400;
                await ctx.Response.WriteAsync($"Channel not found [id: {channel_id}]");
                return;
            }

            var member = channel.Planet.Members.FirstOrDefault();

            if (member == null)
            {
                ctx.Response.StatusCode = 401;
                await ctx.Response.WriteAsync("Member not found");
                return;
            }

            if (!await channel.HasPermission(member, ChatChannelPermissions.View, db))
            {
                ctx.Response.StatusCode = 401;
                await ctx.Response.WriteAsync("Member lacks ChatChannelPermissions.View");
                return;
            }

            switch (ctx.Request.Method)
            {
                case "GET":
                    {
                        ctx.Response.StatusCode = 200;
                        await ctx.Response.WriteAsJsonAsync(channel.Inherits_Perms);
                        return;
                    }
                case "PUT":
                    {
                        if (!auth.HasScope(UserPermissions.PlanetManagement))
                        {
                            ctx.Response.StatusCode = 401;
                            await ctx.Response.WriteAsync($"Token lacks UserPermissions.PlanetManagement");
                            return;
                        }

                        if (!await channel.HasPermission(member, ChatChannelPermissions.ManageChannel, db))
                        {
                            ctx.Response.StatusCode = 401;
                            await ctx.Response.WriteAsync("Member lacks ChatChannelPermissions.ManageChannel");
                            return;
                        }

                        string body = await ctx.Request.ReadBodyStringAsync();

                        bool inherits;

                        bool parsed = bool.TryParse(body, out inherits);

                        if (!parsed)
                        {
                            ctx.Response.StatusCode = 400;
                            await ctx.Response.WriteAsync("Given value is invalid");
                            return;
                        }

                        await channel.SetInheritsPermsAsync(inherits, db);

                        ctx.Response.StatusCode = 200;
                        await ctx.Response.WriteAsync("Success");
                        return;
                    }
            }
        }

        private static async Task Name(HttpContext ctx, ValourDB db, ulong channel_id,
                                         [FromHeader] string authorization)
        {
            AuthToken auth = await ServerAuthToken.TryAuthorize(authorization, db);

            if (auth == null)
            {
                ctx.Response.StatusCode = 401;
                await ctx.Response.WriteAsync($"Token is invalid [token: {authorization}]");
                return;
            }

            ServerPlanetChatChannel channel = await db.PlanetChatChannels.Include(x => x.Planet)
                                                                         .ThenInclude(x => x.Members.Where(x => x.User_Id == auth.User_Id))
                                                                         .FirstOrDefaultAsync(x => x.Id == channel_id);

            if (channel == null)
            {
                ctx.Response.StatusCode = 400;
                await ctx.Response.WriteAsync($"Channel not found [id: {channel_id}]");
                return;
            }

            var member = channel.Planet.Members.FirstOrDefault();

            if (member == null)
            {
                ctx.Response.StatusCode = 401;
                await ctx.Response.WriteAsync("Member not found");
                return;
            }

            if (!await channel.HasPermission(member, ChatChannelPermissions.View, db))
            {
                ctx.Response.StatusCode = 401;
                await ctx.Response.WriteAsync("Member lacks ChatChannelPermissions.View");
                return;
            }

            switch (ctx.Request.Method)
            {
                case "GET":
                    {
                        ctx.Response.StatusCode = 200;
                        await ctx.Response.WriteAsJsonAsync(channel.Name);
                        return;
                    }
                case "PUT":
                    {
                        if (!auth.HasScope(UserPermissions.PlanetManagement))
                        {
                            ctx.Response.StatusCode = 401;
                            await ctx.Response.WriteAsync($"Token lacks UserPermissions.PlanetManagement");
                            return;
                        }

                        if (!await channel.HasPermission(member, ChatChannelPermissions.ManageChannel, db))
                        {
                            ctx.Response.StatusCode = 401;
                            await ctx.Response.WriteAsync("Member lacks ChatChannelPermissions.ManageChannel");
                            return;
                        }

                        string body = await ctx.Request.ReadBodyStringAsync();

                        var result = await channel.TrySetNameAsync(body, db);

                        if (!result.Success)
                        {
                            ctx.Response.StatusCode = 400;
                        }
                        else
                        {
                            ctx.Response.StatusCode = 200;
                        }

                        await ctx.Response.WriteAsync(result.Message);
                        return;
                    }
            }
        }

        private static async Task Description(HttpContext ctx, ValourDB db, ulong channel_id,
                                         [FromHeader] string authorization)
        {
            AuthToken auth = await ServerAuthToken.TryAuthorize(authorization, db);

            if (auth == null)
            {
                ctx.Response.StatusCode = 401;
                await ctx.Response.WriteAsync($"Token is invalid [token: {authorization}]");
                return;
            }

            ServerPlanetChatChannel channel = await db.PlanetChatChannels.Include(x => x.Planet)
                                                                         .ThenInclude(x => x.Members.Where(x => x.User_Id == auth.User_Id))
                                                                         .FirstOrDefaultAsync(x => x.Id == channel_id);

            if (channel == null)
            {
                ctx.Response.StatusCode = 400;
                await ctx.Response.WriteAsync($"Channel not found [id: {channel_id}]");
                return;
            }

            var member = channel.Planet.Members.FirstOrDefault();

            if (member == null)
            {
                ctx.Response.StatusCode = 401;
                await ctx.Response.WriteAsync("Member not found");
                return;
            }

            if (!await channel.HasPermission(member, ChatChannelPermissions.View, db))
            {
                ctx.Response.StatusCode = 401;
                await ctx.Response.WriteAsync("Member lacks ChatChannelPermissions.View");
                return;
            }

            switch (ctx.Request.Method)
            {
                case "GET":
                    {
                        ctx.Response.StatusCode = 200;
                        await ctx.Response.WriteAsJsonAsync(channel.Description);
                        return;
                    }
                case "PUT":
                    {
                        if (!auth.HasScope(UserPermissions.PlanetManagement))
                        {
                            ctx.Response.StatusCode = 401;
                            await ctx.Response.WriteAsync($"Token lacks UserPermissions.PlanetManagement");
                            return;
                        }

                        if (!await channel.HasPermission(member, ChatChannelPermissions.ManageChannel, db))
                        {
                            ctx.Response.StatusCode = 401;
                            await ctx.Response.WriteAsync("Member lacks ChatChannelPermissions.ManageChannel");
                            return;
                        }

                        string body = await ctx.Request.ReadBodyStringAsync();

                        await channel.SetDescriptionAsync(body, db);

                        ctx.Response.StatusCode = 200;
                        await ctx.Response.WriteAsync("Success");
                        return;
                    }
            }
        }

        private static async Task ParentId(HttpContext ctx, ValourDB db, ulong channel_id,
                                         [FromHeader] string authorization)
        {
            AuthToken auth = await ServerAuthToken.TryAuthorize(authorization, db);

            if (auth == null)
            {
                ctx.Response.StatusCode = 401;
                await ctx.Response.WriteAsync($"Token is invalid [token: {authorization}]");
                return;
            }

            ServerPlanetChatChannel channel = await db.PlanetChatChannels.Include(x => x.Planet)
                                                                         .ThenInclude(x => x.Members.Where(x => x.User_Id == auth.User_Id))
                                                                         .FirstOrDefaultAsync(x => x.Id == channel_id);

            if (channel == null)
            {
                ctx.Response.StatusCode = 400;
                await ctx.Response.WriteAsync($"Channel not found [id: {channel_id}]");
                return;
            }

            var member = channel.Planet.Members.FirstOrDefault();

            if (member == null)
            {
                ctx.Response.StatusCode = 401;
                await ctx.Response.WriteAsync("Member not found");
                return;
            }

            if (!await channel.HasPermission(member, ChatChannelPermissions.View, db))
            {
                ctx.Response.StatusCode = 401;
                await ctx.Response.WriteAsync("Member lacks ChatChannelPermissions.View");
                return;
            }

            switch (ctx.Request.Method)
            {
                case "GET":
                    {
                        ctx.Response.StatusCode = 200;
                        await ctx.Response.WriteAsJsonAsync(channel.Parent_Id);
                        return;
                    }
                case "PUT":
                    {
                        if (!auth.HasScope(UserPermissions.PlanetManagement))
                        {
                            ctx.Response.StatusCode = 401;
                            await ctx.Response.WriteAsync($"Token lacks UserPermissions.PlanetManagement");
                            return;
                        }

                        if (!await channel.HasPermission(member, ChatChannelPermissions.ManageChannel, db))
                        {
                            ctx.Response.StatusCode = 401;
                            await ctx.Response.WriteAsync("Member lacks ChatChannelPermissions.ManageChannel");
                            return;
                        }

                        string body = await ctx.Request.ReadBodyStringAsync();

                        ulong parent_id;
                        bool parsed = ulong.TryParse(body, out parent_id);

                        if (!parsed)
                        {
                            ctx.Response.StatusCode = 400;
                            await ctx.Response.WriteAsync("Given value is invalid");
                            return;
                        }

                        // Ensure parent category exists and belongs to the same planet
                        var parent = await db.PlanetCategories.FindAsync(parent_id);

                        if (parent == null)
                        {
                            ctx.Response.StatusCode = 400;
                            await ctx.Response.WriteAsync($"Category not found [id: {parent_id}]");
                            return;
                        }

                        if (parent.Planet_Id != channel.Planet_Id)
                        {
                            ctx.Response.StatusCode = 400;
                            await ctx.Response.WriteAsync($"Category belongs to a different planet");
                            return;
                        }

                        await channel.SetParentAsync(parent_id, db);

                        ctx.Response.StatusCode = 200;
                        await ctx.Response.WriteAsync("Success");
                        return;
                    }
            }
        }

        private static async Task PostMessage(HttpContext ctx, ValourDB db,
                                         [FromHeader] string authorization)
        {


            AuthToken auth = await ServerAuthToken.TryAuthorize(authorization, db);

            if (auth == null)
            {
                ctx.Response.StatusCode = 401;
                await ctx.Response.WriteAsync($"Token is invalid [token: {authorization}]");
                return;
            }

            string body = await ctx.Request.ReadBodyStringAsync();

            var message = JsonSerializer.Deserialize<PlanetMessage>(body);

            if (message == null || message.Content == null || message.Fingerprint == null)
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

        private static async Task GetMessages(HttpContext ctx, ValourDB db, ulong channel_id,
                                              [FromHeader] string authorization,
                                              ulong index = ulong.MaxValue, int count = 10)
        {
            // Request parameter validation //
            if (count > 64)
            {
                ctx.Response.StatusCode = 400;
                await ctx.Response.WriteAsync("Max count is 64");
                return;
            }


            // Request authorization //

            AuthToken auth = await ServerAuthToken.TryAuthorize(authorization, db);

            if (auth == null)
            {
                ctx.Response.StatusCode = 401;
                await ctx.Response.WriteAsync($"Token is invalid [token: {authorization}]");
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
    }
}
