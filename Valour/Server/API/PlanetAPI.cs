using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Valour.Server.Categories;
using Valour.Server.Database;
using Valour.Server.Extensions;
using Valour.Server.MPS;
using Valour.Server.MPS.Proxy;
using Valour.Server.Oauth;
using Valour.Server.Planets;
using Valour.Server.Roles;
using Valour.Server.Users;
using Valour.Shared;
using Valour.Shared.Items;
using Valour.Shared.Oauth;
using Valour.Shared.Planets;

/*  Valour - A free and secure chat client
 *  Copyright (C) 2021 Vooper Media LLC
 *  This program is subject to the GNU Affero General Public license
 *  A copy of the license should be included - if not, see <http://www.gnu.org/licenses/>
 */

namespace Valour.Server.API
{
    public static class PlanetAPI
    {
        // Constant planet variables //

        /// <summary>
        /// The maximum planets a user is allowed to have. This will increase after 
        /// the alpha period is complete.
        /// </summary>
        public static int MAX_OWNED_PLANETS = 5;

        /// <summary>
        /// The maximum planets a user is allowed to join. This will increase after the 
        /// alpha period is complete.
        /// </summary>
        public static int MAX_JOINED_PLANETS = 20;

        /// <summary>
        /// Adds the routes for this API section
        /// </summary>
        public static void AddRoutes(WebApplication app)
        {
            app.MapPost("api/planet/create", Create);
            
            app.Map("api/planet/{planet_id}/name", Name);
            app.Map("api/planet/{planet_id}/description", Description);
            app.Map("api/planet/{planet_id}/public", Public);
            app.Map("api/planet/{planet_id}", Planet);
            app.Map("api/planet/{planet_id}/primary_channel", PrimaryChannel);

            app.MapPost("api/planet/{planet_id}/channels", CreateChannel);
            app.MapGet("api/planet/{planet_id}/channels", GetChannels);
            app.MapGet("api/planet/{planet_id}/channel_ids", GetChannelIds);

            app.MapPost("api/planet/{planet_id}/categories", CreateCategory);
            app.MapGet("api/planet/{planet_id}/categories", GetCategories);

            app.MapGet("api/planet/{planet_id}/member_info", GetMemberInfo);

            app.MapGet("api/planet/{planet_id}/roles", GetRoles);

            app.MapPost("api/planet/{planet_id}/members/{target_id}/kick", KickMember);
            app.MapPost("api/planet/{planet_id}/members/{target_id}/ban", BanMember);
        }
        
        private static async Task BanMember(HttpContext ctx, ValourDB db, ulong planet_id, ulong target_id,
            [FromHeader] string authorization, string reason = "None provided", uint duration = 0)
        {
            AuthToken auth = await ServerAuthToken.TryAuthorize(authorization, db);
            if (auth is null) { Results.Unauthorized(); return; }
            
            ServerPlanet planet = await db.Planets.Include(x => x.Members.Where(x => x.User_Id == auth.User_Id || x.Id == target_id))
                .FirstOrDefaultAsync(x => x.Id == planet_id);

            if (planet is null)
            {
                ctx.Response.StatusCode = 404;
                await ctx.Response.WriteAsync($"Planet not found");
                return;
            }

            ServerPlanetMember member = planet.Members.FirstOrDefault(x => x.User_Id == auth.User_Id);
            ServerPlanetMember target = planet.Members.FirstOrDefault(x => x.Id == target_id);
            
            var result = await planet.BanMemberAsync(member, target, reason, duration, db);
            
            ctx.Response.StatusCode = result.Data;
            await ctx.Response.WriteAsync(result.Message);
        }
        
        private static async Task KickMember(HttpContext ctx, ValourDB db, ulong planet_id, ulong target_id,
            [FromHeader] string authorization)
        {
            AuthToken auth = await ServerAuthToken.TryAuthorize(authorization, db);
            if (auth is null) { Results.Unauthorized(); return; }

            ServerPlanet planet = await db.Planets.Include(x => x.Members.Where(x => x.User_Id == auth.User_Id || x.Id == target_id))
                .FirstOrDefaultAsync(x => x.Id == planet_id);

            if (planet is null)
            {
                ctx.Response.StatusCode = 404;
                await ctx.Response.WriteAsync($"Planet not found");
                return;
            }

            ServerPlanetMember member = planet.Members.FirstOrDefault(x => x.User_Id == auth.User_Id);
            ServerPlanetMember target = planet.Members.FirstOrDefault(x => x.Id == target_id);
            
            var result = await planet.KickMemberAsync(member, target, db);
            
            ctx.Response.StatusCode = result.Data;
            await ctx.Response.WriteAsync(result.Message);
        }

        private static async Task Planet(HttpContext ctx, ValourDB db, ulong planet_id,
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
                ctx.Response.StatusCode = 401;
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

            switch (ctx.Request.Method)
            {
                case "GET":
                {
                    // All members have access to the planet object by default.
                    // I really have no clue why they wouldn't, so I'm not adding a View
                    // permissions test. Fight me.
                    ctx.Response.StatusCode = 200;
                    await ctx.Response.WriteAsJsonAsync((Planet) planet);
                    return;
                }
                case "DELETE":
                {
                    // User MUST be the owner of the planet
                    if (planet.Owner_Id != auth.User_Id)
                    {
                        ctx.Response.StatusCode = 401;
                        await ctx.Response.WriteAsync("Only owner can delete");
                        return;
                    }

                    return;
                }
            }
        }

        private static async Task Name(HttpContext ctx, ValourDB db, ulong planet_id,
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

            switch (ctx.Request.Method)
            {
                case "GET":
                {
                    ctx.Response.StatusCode = 200;
                    await ctx.Response.WriteAsJsonAsync(planet.Name);
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

                    if (!await planet.HasPermissionAsync(member, PlanetPermissions.Manage, db))
                    {
                        ctx.Response.StatusCode = 401;
                        await ctx.Response.WriteAsync("Member lacks PlanetPermissions.Manage");
                        return;
                    }

                    string body = await ctx.Request.ReadBodyStringAsync();

                    var result = await planet.TrySetNameAsync(body, db);

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

        private static async Task Description(HttpContext ctx, ValourDB db, ulong planet_id,
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

            switch (ctx.Request.Method)
            {
                case "GET":
                {
                    ctx.Response.StatusCode = 200;
                    await ctx.Response.WriteAsJsonAsync(planet.Description);
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

                    if (!await planet.HasPermissionAsync(member, PlanetPermissions.Manage, db))
                    {
                        ctx.Response.StatusCode = 401;
                        await ctx.Response.WriteAsync("Member lacks PlanetPermissions.Manage");
                        return;
                    }

                    string body = await ctx.Request.ReadBodyStringAsync();

                    var result = await planet.TrySetDescriptionAsync(body, db);

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

        private static async Task Public(HttpContext ctx, ValourDB db, ulong planet_id,
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

            switch (ctx.Request.Method)
            {
                case "GET":
                {
                    ctx.Response.StatusCode = 200;
                    await ctx.Response.WriteAsJsonAsync(planet.Public);
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

                    if (!await planet.HasPermissionAsync(member, PlanetPermissions.Manage, db))
                    {
                        ctx.Response.StatusCode = 401;
                        await ctx.Response.WriteAsync("Member lacks PlanetPermissions.Manage");
                        return;
                    }

                    string body = await ctx.Request.ReadBodyStringAsync();

                    bool parsed = false;
                    parsed = bool.TryParse(body, out var in_public);

                    if (!parsed)
                    {
                        ctx.Response.StatusCode = 400;
                        await ctx.Response.WriteAsync("Failed to parse body");
                        return;
                    }

                    var result = await planet.TrySetPublicAsync(in_public, db);

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

        private static async Task Create(HttpContext ctx, ValourDB db,
            [FromHeader] string authorization, [Required] string name,
            [Required] string image_url)
        {
            ServerAuthToken auth = await ServerAuthToken.TryAuthorize(authorization, db);

            if (auth == null)
            {
                ctx.Response.StatusCode = 401;
                await ctx.Response.WriteAsync($"Token is invalid [token: {authorization}]");
                return;
            }

            TaskResult nameValid = ServerPlanet.ValidateName(name);

            if (!nameValid.Success)
            {
                ctx.Response.StatusCode = 400;
                await ctx.Response.WriteAsync(nameValid.Message);
                return;
            }

            ServerUser user = await db.Users.FindAsync(auth.User_Id);

            if (!user.Valour_Staff)
            {
                var owned_planets = await db.Planets.CountAsync(x => x.Owner_Id == user.Id);

                if (owned_planets > MAX_OWNED_PLANETS)
                {
                    ctx.Response.StatusCode = 400;
                    await ctx.Response.WriteAsync("Max owned planets reached");
                    return;
                }
            }

            // Image handling via proxy
            ProxyResponse proxyResponse = await MPSManager.GetProxy(image_url);

            bool is_media = MPSManager.Media_Types.Contains(proxyResponse.Item.Mime_Type);

            if (proxyResponse.Item == null || !is_media)
            {
                image_url = "https://valour.gg/image.png";
            }
            else
            {
                image_url = proxyResponse.Item.Url;
            }

            ulong planet_id = IdManager.Generate();

            // Create general category
            ServerPlanetCategory category = new ServerPlanetCategory()
            {
                Id = IdManager.Generate(),
                Name = "General",
                Parent_Id = null,
                Planet_Id = planet_id,
                Description = "General category",
                Position = 0
            };

            // Create general channel
            ServerPlanetChatChannel channel = new ServerPlanetChatChannel()
            {
                Id = IdManager.Generate(),
                Planet_Id = planet_id,
                Name = "General",
                Message_Count = 0,
                Description = "General chat channel",
                Parent_Id = category.Id
            };

            // Create default role
            ServerPlanetRole defaultRole = new ServerPlanetRole()
            {
                Id = IdManager.Generate(),
                Planet_Id = planet_id,
                Position = uint.MaxValue,
                Color_Blue = 255,
                Color_Green = 255,
                Color_Red = 255,
                Name = "@everyone"
            };

            ServerPlanet planet = new ServerPlanet()
            {
                Id = planet_id,
                Name = name,
                Member_Count = 1,
                Description = "A Valour server.",
                Image_Url = image_url,
                Public = true,
                Owner_Id = user.Id,
                Default_Role_Id = defaultRole.Id,
                Main_Channel_Id = channel.Id
            };

            // Add planet to database
            await db.Planets.AddAsync(planet);
            await db.SaveChangesAsync(); // We must do this first to prevent foreign key errors

            // Add category to database
            await db.PlanetCategories.AddAsync(category);
            // Add channel to database
            await db.PlanetChatChannels.AddAsync(channel);
            // Add default role to database
            await db.PlanetRoles.AddAsync(defaultRole);
            // Save changes
            await db.SaveChangesAsync();
            // Add owner to planet
            await planet.AddMemberAsync(user);

            ctx.Response.StatusCode = 200;
            await ctx.Response.WriteAsJsonAsync(planet.Id);
        }

        private static async Task GetChannels(HttpContext ctx, ValourDB db,
            [FromHeader] string authorization, [Required] ulong planet_id)
        {
            ServerAuthToken auth = await ServerAuthToken.TryAuthorize(authorization, db);

            if (auth == null)
            {
                ctx.Response.StatusCode = 401;
                await ctx.Response.WriteAsync($"Token is invalid [token: {authorization}]");
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

        private static async Task GetChannelIds(HttpContext ctx, ValourDB db,
            [FromHeader] string authorization, [Required] ulong planet_id)
        {
            ServerAuthToken auth = await ServerAuthToken.TryAuthorize(authorization, db);

            if (auth == null)
            {
                ctx.Response.StatusCode = 401;
                await ctx.Response.WriteAsync($"Token is invalid [token: {authorization}]");
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

        private static async Task CreateChannel(HttpContext ctx, ValourDB db,
            [FromHeader] string authorization)
        {
            string body = await ctx.Request.ReadBodyStringAsync();

            if (string.IsNullOrWhiteSpace(body))
            {
                ctx.Response.StatusCode = 400;
                await ctx.Response.WriteAsync("Please include channel in body");
                return;
            }

            ServerPlanetChatChannel channel_data = JsonSerializer.Deserialize<ServerPlanetChatChannel>(body);

            if (channel_data == null)
            {
                ctx.Response.StatusCode = 400;
                await ctx.Response.WriteAsync("Please include channel in body");
                return;
            }

            if (string.IsNullOrWhiteSpace(channel_data.Name))
            {
                ctx.Response.StatusCode = 400;
                await ctx.Response.WriteAsync("Please include a channel name");
                return;
            }

            // Request parameter validation //

            TaskResult name_valid = ServerPlanetChatChannel.ValidateName(channel_data.Name);

            if (!name_valid.Success)
            {
                ctx.Response.StatusCode = 400;
                await ctx.Response.WriteAsync($"Name is not valid [name: {channel_data.Name}]");
                return;
            }

            // Request authorization //

            AuthToken auth = await ServerAuthToken.TryAuthorize(authorization, db);

            if (!auth.HasScope(UserPermissions.PlanetManagement))
            {
                ctx.Response.StatusCode = 401;
                await ctx.Response.WriteAsync("Token lacks UserPermissions.PlanetManagement scope");
                return;
            }

            ServerPlanet planet = await db.Planets.Include(x => x.Members.Where(x => x.User_Id == auth.User_Id))
                .FirstOrDefaultAsync(x => x.Id == channel_data.Planet_Id);

            var member = planet.Members.FirstOrDefault();

            if (!await planet.HasPermissionAsync(member, PlanetPermissions.ManageChannels, db))
            {
                ctx.Response.StatusCode = 401;
                await ctx.Response.WriteAsync("Member lacks PlanetPermissions.ManageChannels node");
                return;
            }

            // Ensure parent category exists

            ServerPlanetCategory parent = await db.PlanetCategories.FindAsync(channel_data.Parent_Id);

            if (parent == null)
            {
                ctx.Response.StatusCode = 400;
                await ctx.Response.WriteAsync("Could not find parent");
                return;
            }

            if (parent.Planet_Id != planet.Id)
            {
                ctx.Response.StatusCode = 400;
                await ctx.Response.WriteAsync("Parent id does not match planet");
                return;
            }

            // Request action //

            // Creates the channel

            ushort child_count = 0;

            child_count += (ushort) await db.PlanetChatChannels.CountAsync(x => x.Parent_Id == channel_data.Parent_Id);
            child_count += (ushort) await db.PlanetCategories.CountAsync(x => x.Parent_Id == channel_data.Parent_Id);

            ServerPlanetChatChannel channel = new ServerPlanetChatChannel()
            {
                Id = IdManager.Generate(),
                Name = channel_data.Name,
                Planet_Id = channel_data.Planet_Id,
                Parent_Id = channel_data.Parent_Id,
                Message_Count = 0,
                Description = channel_data.Description,
                Position = child_count
            };

            // Add channel to database
            await db.PlanetChatChannels.AddAsync(channel);

            // Save changes to DB
            await db.SaveChangesAsync();

            // Send channel refresh
            PlanetHub.NotifyChatChannelChange(channel);

            ctx.Response.StatusCode = 201;
            await ctx.Response.WriteAsync(channel.Id.ToString());
        }

        private static async Task CreateCategory(HttpContext ctx, ValourDB db,
            [FromHeader] string authorization)
        {
            string body = await ctx.Request.ReadBodyStringAsync();

            if (string.IsNullOrWhiteSpace(body))
            {
                ctx.Response.StatusCode = 400;
                await ctx.Response.WriteAsync("Please include category in body");
                return;
            }

            ServerPlanetCategory category_data = JsonSerializer.Deserialize<ServerPlanetCategory>(body);

            if (category_data == null)
            {
                ctx.Response.StatusCode = 400;
                await ctx.Response.WriteAsync("Please include category in body");
                return;
            }

            if (string.IsNullOrWhiteSpace(category_data.Name))
            {
                ctx.Response.StatusCode = 400;
                await ctx.Response.WriteAsync("Please include a category name");
                return;
            }

            // Request parameter validation //

            TaskResult name_valid = ServerPlanetCategory.ValidateName(category_data.Name);

            if (!name_valid.Success)
            {
                ctx.Response.StatusCode = 400;
                await ctx.Response.WriteAsync($"Name is not valid [name: {category_data.Name}]");
                return;
            }

            // Request authorization //

            AuthToken auth = await ServerAuthToken.TryAuthorize(authorization, db);

            if (!auth.HasScope(UserPermissions.PlanetManagement))
            {
                ctx.Response.StatusCode = 401;
                await ctx.Response.WriteAsync("Token lacks UserPermissions.PlanetManagement scope");
                return;
            }

            ServerPlanet planet = await db.Planets.Include(x => x.Members.Where(x => x.User_Id == auth.User_Id))
                .FirstOrDefaultAsync(x => x.Id == category_data.Planet_Id);

            var member = planet.Members.FirstOrDefault();

            if (!await planet.HasPermissionAsync(member, PlanetPermissions.ManageChannels, db))
            {
                ctx.Response.StatusCode = 401;
                await ctx.Response.WriteAsync("Member lacks PlanetPermissions.ManageChannels node");
                return;
            }

            // Ensure parent category exists

            ulong? parent_id = null;

            ServerPlanetCategory parent = await db.PlanetCategories.FindAsync(category_data.Parent_Id);

            ushort child_count = 0;

            if (parent != null)
            {
                parent_id = parent.Id;

                if (parent.Planet_Id != planet.Id)
                {
                    ctx.Response.StatusCode = 400;
                    await ctx.Response.WriteAsync("Parent id does not match planet");
                    return;
                }

                child_count += (ushort) await db.PlanetChatChannels.CountAsync(x => x.Parent_Id == parent_id);
                child_count += (ushort) await db.PlanetCategories.CountAsync(x => x.Parent_Id == parent_id);
            }

            // Request action //

            // Creates the category

            ServerPlanetCategory category = new ServerPlanetCategory()
            {
                Id = IdManager.Generate(),
                Name = category_data.Name,
                Planet_Id = category_data.Planet_Id,
                Parent_Id = category_data.Parent_Id,
                Description = category_data.Description,
                Position = child_count
            };

            // Add channel to database
            await db.PlanetCategories.AddAsync(category);

            // Save changes to DB
            await db.SaveChangesAsync();

            // Send channel refresh
            PlanetHub.NotifyCategoryChange(category);

            ctx.Response.StatusCode = 201;
            await ctx.Response.WriteAsync(category.Id.ToString());
        }

        private static async Task GetCategories(HttpContext ctx, ValourDB db,
            [FromHeader] string authorization, [Required] ulong planet_id)
        {
            ServerAuthToken auth = await ServerAuthToken.TryAuthorize(authorization, db);

            if (auth == null)
            {
                ctx.Response.StatusCode = 401;
                await ctx.Response.WriteAsync($"Token is invalid [token: {authorization}]");
                return;
            }

            ServerPlanet planet = await db.Planets.Include(x => x.Members.Where(x => x.User_Id == auth.User_Id))
                .Include(x => x.Categories)
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

            List<ServerPlanetCategory> result = new List<ServerPlanetCategory>();

            foreach (var category in planet.Categories)
            {
                if (await category.HasPermission(member, CategoryPermissions.View, db))
                {
                    result.Add(category);
                }
            }

            ctx.Response.StatusCode = 200;
            await ctx.Response.WriteAsJsonAsync(result);
        }

        private static async Task PrimaryChannel(HttpContext ctx, ValourDB db, ulong planet_id,
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

            switch (ctx.Request.Method)
            {
                case "GET":
                {
                    ServerPlanetChatChannel mainChannel = await db.PlanetChatChannels.FindAsync(planet.Main_Channel_Id);

                    if (mainChannel == null)
                    {
                        ctx.Response.StatusCode = 400;
                        await ctx.Response.WriteAsync($"Main channel not found [id: {planet.Main_Channel_Id}]\n" +
                                                      $"Bug a developer, this should not happen.");

                        return;
                    }

                    ctx.Response.StatusCode = 200;
                    await ctx.Response.WriteAsJsonAsync(mainChannel);
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

                    if (!await planet.HasPermissionAsync(member, PlanetPermissions.Manage, db))
                    {
                        ctx.Response.StatusCode = 401;
                        await ctx.Response.WriteAsync("Member lacks PlanetPermissions.Manage");
                        return;
                    }

                    string body = await ctx.Request.ReadBodyStringAsync();

                    ServerPlanetChatChannel in_channel = JsonSerializer.Deserialize<ServerPlanetChatChannel>(body);

                    if (in_channel == null)
                    {
                        ctx.Response.StatusCode = 400;
                        await ctx.Response.WriteAsync($"Could not deserialize channel");
                        return;
                    }

                    ServerPlanetChatChannel channel = await db.PlanetChatChannels.FindAsync(in_channel.Id);

                    if (channel == null)
                    {
                        ctx.Response.StatusCode = 400;
                        await ctx.Response.WriteAsync($"Could not find channel [id: {in_channel.Id}]");
                        return;
                    }

                    if (channel.Planet.Id != planet.Id)
                    {
                        ctx.Response.StatusCode = 400;
                        await ctx.Response.WriteAsync($"Channel belongs to different planet");
                        return;
                    }

                    planet.Main_Channel_Id = channel.Id;
                    await db.SaveChangesAsync();

                    ctx.Response.StatusCode = 200;
                    await ctx.Response.WriteAsync("Success");

                    return;
                }
            }
        }

        private static async Task GetMemberInfo(HttpContext ctx, ValourDB db,
            [FromHeader] string authorization, [Required] ulong planet_id)
        {
            ServerAuthToken auth = await ServerAuthToken.TryAuthorize(authorization, db);

            if (auth == null)
            {
                ctx.Response.StatusCode = 401;
                await ctx.Response.WriteAsync($"Token is invalid [token: {authorization}]");
                return;
            }
            
            ServerPlanet planet = await db.Planets
                .Include(x => x.Members).ThenInclude(x => x.User)
                .Include(x => x.Members).ThenInclude(x => x.RoleMembership.OrderBy(x => x.Role.Position))
                .ThenInclude(x => x.Role)
                .FirstOrDefaultAsync(x => x.Id == planet_id);

            if (planet == null)
            {
                ctx.Response.StatusCode = 400;
                await ctx.Response.WriteAsync($"Planet not found [id: {planet_id.ToString()}]");
                return;
            }
            
            if (!planet.Members.Any(x => x.User_Id == auth.User_Id))
            {
                ctx.Response.StatusCode = 401;
                await ctx.Response.WriteAsync($"Member not found");
                return;
            }
            
            List<PlanetMemberInfo> info = new List<PlanetMemberInfo>();

            foreach (var member in planet.Members)
            {
                PlanetMemberInfo planetInfo = new PlanetMemberInfo()
                {
                    Member = member,
                    User = member.User,
                    RoleIds = member.RoleMembership.Select(x => x.Role_Id),
                    State = "Currently browsing"
                };

                info.Add(planetInfo);
            }
            
            ctx.Response.StatusCode = 200;
            await ctx.Response.WriteAsJsonAsync(info);
        }
        
        private static async Task GetRoles(HttpContext ctx, ValourDB db, ulong planet_id,
            [FromHeader] string authorization)
        {
            ServerAuthToken auth = await ServerAuthToken.TryAuthorize(authorization, db);

            if (auth == null)
            {
                ctx.Response.StatusCode = 401;
                await ctx.Response.WriteAsync($"Token is invalid");
                return;
            }

            ServerPlanet planet = await db.Planets
                .Include(x => x.Members.Where(x => x.User_Id == auth.User_Id))
                .Include(x => x.Roles)
                .FirstOrDefaultAsync(x => x.Id == planet_id);

            if (planet == null)
            {
                ctx.Response.StatusCode = 404;
                await ctx.Response.WriteAsync($"Planet not found [id: {planet_id.ToString()}]");
                return;
            }

            var member = planet.Members.FirstOrDefault();

            if (member == null)
            {
                ctx.Response.StatusCode = 403;
                await ctx.Response.WriteAsync($"Member not found");
                return;
            }

            if (!await planet.HasPermissionAsync(member, PlanetPermissions.View, db))
            {
                ctx.Response.StatusCode = 403;
                await ctx.Response.WriteAsync($"Member lacks PlanetPermissions.View");
                return;
            }

            ctx.Response.StatusCode = 200;
            await ctx.Response.WriteAsJsonAsync(planet.Roles);
        }
    }
}