using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Threading.Tasks;
using Valour.Server.Categories;
using Valour.Server.Database;
using Valour.Server.MPS;
using Valour.Server.MPS.Proxy;
using Valour.Server.Oauth;
using Valour.Server.Planets;
using Valour.Server.Roles;
using Valour.Server.Users;
using Valour.Shared;
using Valour.Shared.Channels;
using Valour.Shared.Oauth;
using Valour.Shared.Planets;

/*  Valour - A free and secure chat client
 *  Copyright (C) 2021 Vooper Media LLC
 *  This program is subject to the GNU Affero General Public license
 *  A copy of the license should be included - if not, see <http://www.gnu.org/licenses/>
 */

namespace Valour.Server.API
{
    public class PlanetAPI
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
            app.MapPost("/api/planet/create", Create);


            app.Map("/api/planet/{planet_id}/name", Name);

            app.Map("/api/planet/{planet_id}", Planet);
            app.MapGet("/api/planet/{planet_id}/channels/", GetChannels);
            app.MapGet("/api/planet/{planet_id}/channelids/", GetChannelIds);
            
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
                        await ctx.Response.WriteAsJsonAsync((Planet)planet);
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
            string name = "";

            AuthToken auth = await ServerAuthToken.TryAuthorize(authorization, db);

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

            ServerPlanet planet = await db.Planets.Include(x => x.Members.Where(x => x.User_Id == auth.User_Id))
                                                  .FirstOrDefaultAsync(x => x.Id == planet_id);

            if (planet == null)
            {
                ctx.Response.StatusCode = 400;
                await ctx.Response.WriteAsync($"Planet not found [id: {planet_id}]");
                return;
            }

            ServerPlanetMember member = planet.Members.FirstOrDefault();

            if (!await planet.HasPermissionAsync(member, PlanetPermissions.Manage, db))
            {
                ctx.Response.StatusCode = 401;
                await ctx.Response.WriteAsync("Member lacks PlanetPermissions.Manage");
                return;
            }

            planet.Name = name;
            await db.SaveChangesAsync();

            ctx.Response.StatusCode = 200;
            await ctx.Response.WriteAsync("Success");
        }

        /// <summary>
        /// Attempts to create a planet
        /// </summary>
        //
        // Type:
        // POST
        // -----------------------------------
        //
        // Route:
        // /api/planet/create
        // -----------------------------------
        //
        // Query parameters:
        // --------------------------------------------
        // | token     | Authentication key  | string |
        // | name      | Name of the planet  | string |
        // | image_url | Icon for the planet | string |
        // --------------------------------------------
        //
        /// <returns>
        /// The created Planet object
        /// </returns>
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
            await ctx.Response.WriteAsJsonAsync((Planet)planet);
        }

        /// <summary>
        /// Returns the currently visible channels in a planet
        /// </summary>

        // Type:
        // GET
        // -----------------------------------
        //
        // Route:
        // /api/planet/getchannels
        // -----------------------------------
        //
        // Query parameters:
        // --------------------------------------------
        // | token      | Authentication key | string |
        // | planet_id  | Id of the planet   | ulong  |
        // --------------------------------------------
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

            List<PlanetChatChannel> result = new List<PlanetChatChannel>();

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
    }
}
