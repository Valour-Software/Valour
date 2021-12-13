using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;
using Valour.Database;
using Valour.Database.Items.Authorization;
using Valour.Database.Items.Planets;
using Valour.Database.Items.Planets.Channels;
using Valour.Server.Extensions;
using Valour.Shared;
using Valour.Shared.Authorization;
using Valour.Shared.Categories;
using Valour.Shared.Items;

namespace Valour.Server.API
{
    public class CategoryAPI : BaseAPI
    {
        public static void AddRoutes(WebApplication app)
        {
            app.Map("api/category/{category_id}", Category);
            app.Map("api/category/{category_id}/name", Name);
            app.Map("api/category/{category_id}/parent_id", ParentId);
            app.Map("api/category/{category_id}/description", Description);
            //app.Map("/api/category/{category_id}/inherits_perms", PermissionsInherit);

            app.MapGet ("api/category/{category_id}/children", GetChildren);
            app.MapPost("api/category/{category_id}/children", InsertItem);
            app.MapPost("api/category/{category_id}/children/order", SetChildOrder);
        }

        private static async Task Category(HttpContext ctx, ValourDB db, ulong category_id,
                                          [FromHeader] string authorization)
        {
            var auth = await AuthToken.TryAuthorize(authorization, db);

            if (auth == null)
            {
                ctx.Response.StatusCode = 401;
                await ctx.Response.WriteAsync($"Token is invalid [token: {authorization}]");
                return;
            }

            PlanetCategory category = await db.PlanetCategories.Include(x => x.Planet)
                                                                     .ThenInclude(x => x.Members.Where(x => x.User_Id == auth.User_Id))
                                                                     .FirstOrDefaultAsync(x => x.Id == category_id);

            if (category == null)
            {
                ctx.Response.StatusCode = 400;
                await ctx.Response.WriteAsync($"Category not found [id: {category_id}]");
                return;
            }

            var member = category.Planet.Members.FirstOrDefault();

            if (member == null)
            {
                ctx.Response.StatusCode = 401;
                await ctx.Response.WriteAsync("Member not found");
                return;
            }

            if (!await category.HasPermission(member, CategoryPermissions.View, db))
            {
                ctx.Response.StatusCode = 401;
                await ctx.Response.WriteAsync("Member lacks CategoryPermissions.View");
                return;
            }

            switch (ctx.Request.Method)
            {
                case "GET":
                    {
                        ctx.Response.StatusCode = 200;
                        await ctx.Response.WriteAsJsonAsync(category);
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

                        if (!await category.HasPermission(member, CategoryPermissions.ManageCategory, db))
                        {
                            ctx.Response.StatusCode = 401;
                            await ctx.Response.WriteAsync("Member lacks CategoryPermissions.ManageCategory");
                            return;
                        }

                        TaskResult result = await category.TryDeleteAsync(db);

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

        private static async Task Name(HttpContext ctx, ValourDB db, ulong category_id,
                                      [FromHeader] string authorization)
        {
            var auth = await AuthToken.TryAuthorize(authorization, db);

            if (auth == null)
            {
                ctx.Response.StatusCode = 401;
                await ctx.Response.WriteAsync($"Token is invalid [token: {authorization}]");
                return;
            }

            PlanetCategory category = await db.PlanetCategories.Include(x => x.Planet)
                                                                     .ThenInclude(x => x.Members.Where(x => x.User_Id == auth.User_Id))
                                                                     .FirstOrDefaultAsync(x => x.Id == category_id);

            if (category == null)
            {
                ctx.Response.StatusCode = 400;
                await ctx.Response.WriteAsync($"Category not found [id: {category_id}]");
                return;
            }

            var member = category.Planet.Members.FirstOrDefault();

            if (member == null)
            {
                ctx.Response.StatusCode = 401;
                await ctx.Response.WriteAsync("Member not found");
                return;
            }

            if (!await category.HasPermission(member, CategoryPermissions.View, db))
            {
                ctx.Response.StatusCode = 401;
                await ctx.Response.WriteAsync("Member lacks CategoryPermissions.View");
                return;
            }

            switch (ctx.Request.Method)
            {
                case "GET":
                    {
                        ctx.Response.StatusCode = 200;
                        await ctx.Response.WriteAsJsonAsync(category.Name);
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

                        if (!await category.HasPermission(member, CategoryPermissions.ManageCategory, db))
                        {
                            ctx.Response.StatusCode = 401;
                            await ctx.Response.WriteAsync("Member lacks CategoryPermissions.ManageCategory");
                            return;
                        }

                        string body = await ctx.Request.ReadBodyStringAsync();

                        var result = await category.TrySetNameAsync(body, db);

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

        private static async Task Description(HttpContext ctx, ValourDB db, ulong category_id,
                                      [FromHeader] string authorization)
        {
            var auth = await AuthToken.TryAuthorize(authorization, db);

            if (auth == null)
            {
                ctx.Response.StatusCode = 401;
                await ctx.Response.WriteAsync($"Token is invalid [token: {authorization}]");
                return;
            }

            PlanetCategory category = await db.PlanetCategories.Include(x => x.Planet)
                                                                     .ThenInclude(x => x.Members.Where(x => x.User_Id == auth.User_Id))
                                                                     .FirstOrDefaultAsync(x => x.Id == category_id);

            if (category == null)
            {
                ctx.Response.StatusCode = 400;
                await ctx.Response.WriteAsync($"Category not found [id: {category_id}]");
                return;
            }

            var member = category.Planet.Members.FirstOrDefault();

            if (member == null)
            {
                ctx.Response.StatusCode = 401;
                await ctx.Response.WriteAsync("Member not found");
                return;
            }

            if (!await category.HasPermission(member, CategoryPermissions.View, db))
            {
                ctx.Response.StatusCode = 401;
                await ctx.Response.WriteAsync("Member lacks CategoryPermissions.View");
                return;
            }

            switch (ctx.Request.Method)
            {
                case "GET":
                    {
                        ctx.Response.StatusCode = 200;
                        await ctx.Response.WriteAsJsonAsync(category.Name);
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

                        if (!await category.HasPermission(member, CategoryPermissions.ManageCategory, db))
                        {
                            ctx.Response.StatusCode = 401;
                            await ctx.Response.WriteAsync("Member lacks CategoryPermissions.ManageCategory");
                            return;
                        }

                        string body = await ctx.Request.ReadBodyStringAsync();

                        await category.SetDescriptionAsync(body, db);

                        ctx.Response.StatusCode = 200;
                        await ctx.Response.WriteAsync("Success");
                        return;
                    }
            }
        }

        private static async Task ParentId(HttpContext ctx, ValourDB db, ulong category_id, int? position,
                                      [FromHeader] string authorization)
        {
            if (position == null)
            {
                position = -1;
            }

            var auth = await AuthToken.TryAuthorize(authorization, db);

            if (auth == null)
            {
                ctx.Response.StatusCode = 401;
                await ctx.Response.WriteAsync($"Token is invalid [token: {authorization}]");
                return;
            }

            PlanetCategory category = await db.PlanetCategories.Include(x => x.Planet)
                                                                     .ThenInclude(x => x.Members.Where(x => x.User_Id == auth.User_Id))
                                                                     .FirstOrDefaultAsync(x => x.Id == category_id);

            if (category == null)
            {
                ctx.Response.StatusCode = 400;
                await ctx.Response.WriteAsync($"Category not found [id: {category_id}]");
                return;
            }

            var member = category.Planet.Members.FirstOrDefault();

            if (member == null)
            {
                ctx.Response.StatusCode = 401;
                await ctx.Response.WriteAsync("Member not found");
                return;
            }

            if (!await category.HasPermission(member, CategoryPermissions.View, db))
            {
                ctx.Response.StatusCode = 401;
                await ctx.Response.WriteAsync("Member lacks CategoryPermissions.View");
                return;
            }

            switch (ctx.Request.Method)
            {
                case "GET":
                    {
                        ctx.Response.StatusCode = 200;
                        await ctx.Response.WriteAsJsonAsync(category.Parent_Id);
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

                        string body = await ctx.Request.ReadBodyStringAsync();

                        ulong? parent_id;

                        if (body == "null" || body == "0" || body == "none" || string.IsNullOrWhiteSpace(body))
                        {
                            parent_id = null;
                        }
                        else
                        {
                            ulong parsed_ul;
                            bool parsed = ulong.TryParse(body, out parsed_ul);

                            parent_id = parsed_ul;

                            if (!parsed)
                            {
                                ctx.Response.StatusCode = 400;
                                await ctx.Response.WriteAsync("Given value is invalid");
                                return;
                            }
                        }

                        TaskResult<int> result = await category.TrySetParentAsync(member, parent_id, (int)position, db);

                        ctx.Response.StatusCode = result.Data;
                        await ctx.Response.WriteAsync(result.Message);
                        return;
                    }
            }
        }

        private static async Task GetChildren(HttpContext ctx, ValourDB db, ulong category_id,
                                             [FromHeader] string authorization)
        {
            var auth = await AuthToken.TryAuthorize(authorization, db);

            if (auth == null)
            {
                ctx.Response.StatusCode = 401;
                await ctx.Response.WriteAsync($"Token is invalid [token: {authorization}]");
                return;
            }

            PlanetCategory category = await db.PlanetCategories.Include(x => x.Planet)
                                                                     .ThenInclude(x => x.Members.Where(x => x.User_Id == auth.User_Id))
                                                                     .Include(x => x.Planet)
                                                                     .ThenInclude(x => x.ChatChannels)
                                                                     .Include(x => x.Planet)
                                                                     .ThenInclude(x => x.Categories)
                                                                     .FirstOrDefaultAsync(x => x.Id == category_id);

            if (category == null)
            {
                ctx.Response.StatusCode = 400;
                await ctx.Response.WriteAsync($"Category not found [id: {category_id}]");
                return;
            }

            var member = category.Planet.Members.FirstOrDefault();

            if (member == null)
            {
                ctx.Response.StatusCode = 401;
                await ctx.Response.WriteAsync("Member not found");
                return;
            }

            if (!await category.HasPermission(member, CategoryPermissions.View, db))
            {
                ctx.Response.StatusCode = 401;
                await ctx.Response.WriteAsync("Member lacks CategoryPermissions.View");
                return;
            }

            List<IPlanetChannel> children = new List<IPlanetChannel>();

            foreach (var channel in category.Planet.ChatChannels)
            {
                if (await channel.HasPermission(member, ChatChannelPermissions.View, db))
                {
                    children.Add(channel);
                }
            }

            foreach (var cat in category.Planet.Categories)
            {
                if (await cat.HasPermission(member, CategoryPermissions.View, db))
                {
                    children.Add(cat);
                }
            }

            ctx.Response.StatusCode = 200;
            await ctx.Response.WriteAsJsonAsync(children);
            return;
        }

        private static async Task SetChildOrder(HttpContext ctx, ValourDB db, ulong category_id,
                                               [FromHeader] string authorization)
        {
            var auth = await AuthToken.TryAuthorize(authorization, db);

            if (auth == null)
            {
                ctx.Response.StatusCode = 401;
                await ctx.Response.WriteAsync($"Token is invalid [token: {authorization}]");
                return;
            }

            PlanetCategory category = await db.PlanetCategories.Include(x => x.Planet)
                                                                     .ThenInclude(x => x.Members.Where(x => x.User_Id == auth.User_Id))
                                                                     .FirstOrDefaultAsync(x => x.Id == category_id);

            if (category == null)
            {
                ctx.Response.StatusCode = 400;
                await ctx.Response.WriteAsync($"Category not found [id: {category_id}]");
                return;
            }

            var member = category.Planet.Members.FirstOrDefault();

            if (member == null)
            {
                ctx.Response.StatusCode = 401;
                await ctx.Response.WriteAsync("Member not found");
                return;
            }

            if (!await category.HasPermission(member, CategoryPermissions.View, db))
            {
                ctx.Response.StatusCode = 401;
                await ctx.Response.WriteAsync("Member lacks CategoryPermissions.View");
                return;
            }

            if (!auth.HasScope(UserPermissions.PlanetManagement))
            {
                ctx.Response.StatusCode = 401;
                await ctx.Response.WriteAsync($"Token lacks UserPermissions.PlanetManagement");
                return;
            }

            if (!await category.HasPermission(member, CategoryPermissions.ManageCategory, db))
            {
                ctx.Response.StatusCode = 401;
                await ctx.Response.WriteAsync("Member lacks CategoryPermissions.ManageCategory");
                return;
            }

            string body = await ctx.Request.ReadBodyStringAsync();

            if (string.IsNullOrEmpty(body))
            {
                ctx.Response.StatusCode = 400;
                await ctx.Response.WriteAsync("Include order data.");
                return;
            }

            List<CategoryContentData> orderData = JsonSerializer.Deserialize<List<CategoryContentData>>(body);

            if (orderData == null || orderData.Count == 0)
            {
                ctx.Response.StatusCode = 400;
                await ctx.Response.WriteAsync("Include order data.");
                return;
            }

            List<IPlanetChannel> changed = new List<IPlanetChannel>();

            foreach (CategoryContentData order in orderData)
            {
                IPlanetChannel item = await IPlanetChannel.FindAsync(order.ItemType, order.Id, db);

                if (item == null)
                {
                    ctx.Response.StatusCode = 400;
                    await ctx.Response.WriteAsync($"Item with id {order.Id} not found");
                    return;
                }

                if (item.Planet_Id != category.Planet_Id)
                {
                    ctx.Response.StatusCode = 400;
                    await ctx.Response.WriteAsync($"Item with id {order.Id} belongs to wrong planet {item.Planet_Id}");
                    return;
                }

                // Only act if there is a difference
                if (item.Parent_Id != category_id || item.Position != order.Position)
                {
                    // Prevent putting an item inside of itself
                    if (item.Id != category_id)
                    {
                        item.Parent_Id = category_id;
                        item.Position = order.Position;
                        db.Update(item);
                        changed.Add(item);
                    }
                }
            }

            // If all is successful, save and send updates
            foreach (var item in changed)
            {
                // Send update to clients
                item.NotifyClientsChange();
            }

            await db.SaveChangesAsync();

            ctx.Response.StatusCode = 200;
            await ctx.Response.WriteAsync("Success");
            return;
        }

        private static async Task InsertItem(HttpContext ctx, ValourDB db, ulong category_id, ItemType type,
                                            [FromHeader] string authorization)
        {
            var auth = await AuthToken.TryAuthorize(authorization, db);

            if (auth == null)
            {
                ctx.Response.StatusCode = 401;
                await ctx.Response.WriteAsync($"Token is invalid [token: {authorization}]");
                return;
            }

            PlanetCategory category = await db.PlanetCategories.Include(x => x.Planet)
                                                                     .ThenInclude(x => x.Members.Where(x => x.User_Id == auth.User_Id))
                                                                     .FirstOrDefaultAsync(x => x.Id == category_id);

            if (category == null)
            {
                ctx.Response.StatusCode = 400;
                await ctx.Response.WriteAsync($"Category not found [id: {category_id}]");
                return;
            }

            var member = category.Planet.Members.FirstOrDefault();

            if (member == null)
            {
                ctx.Response.StatusCode = 401;
                await ctx.Response.WriteAsync("Member not found");
                return;
            }

            if (!await category.HasPermission(member, CategoryPermissions.View, db))
            {
                ctx.Response.StatusCode = 401;
                await ctx.Response.WriteAsync("Member lacks CategoryPermissions.View");
                return;
            }

            if (!auth.HasScope(UserPermissions.PlanetManagement))
            {
                ctx.Response.StatusCode = 401;
                await ctx.Response.WriteAsync($"Token lacks UserPermissions.PlanetManagement");
                return;
            }

            if (!await category.HasPermission(member, CategoryPermissions.ManageCategory, db))
            {
                ctx.Response.StatusCode = 401;
                await ctx.Response.WriteAsync("Member lacks CategoryPermissions.ManageCategory");
                return;
            }

            IPlanetChannel in_item = null;

            switch (type)
            {
                case ItemType.Channel:
                    in_item = await JsonSerializer.DeserializeAsync<PlanetChatChannel>(ctx.Request.Body);
                    break;
                case ItemType.Category:
                    in_item = await JsonSerializer.DeserializeAsync<PlanetCategory>(ctx.Request.Body);
                    break;
                default:
                    {
                        ctx.Response.StatusCode = 400;
                        await ctx.Response.WriteAsync("Include valid item type");
                        return;
                    }
            }


            if (in_item == null || in_item.Planet_Id == 0)
            {
                ctx.Response.StatusCode = 400;
                await ctx.Response.WriteAsync("Include item data.");
                return;
            }

            IPlanetChannel item = await IPlanetChannel.FindAsync(in_item.ItemType, in_item.Id, db);

            if (item == null)
            {
                ctx.Response.StatusCode = 400;
                await ctx.Response.WriteAsync($"Item not found [id: {in_item.Id}]");
                return;
            }

            Planet item_planet = await db.Planets.FindAsync(item.Planet_Id);

            if (item_planet == null)
            {
                ctx.Response.StatusCode = 400;
                await ctx.Response.WriteAsync($"Item planet not found [id: {in_item.Planet_Id}]");
                return;
            }

            if (item_planet.Id != category.Planet_Id)
            {
                ctx.Response.StatusCode = 400;
                await ctx.Response.WriteAsync($"Item belongs to different planet");
                return;
            }

            if (item.Parent_Id == category.Id)
            {
                ctx.Response.StatusCode = 200;
                await ctx.Response.WriteAsync($"No change");
                return;
            }

            // Ensure that if this is a category, it is not going into a category that contains itself!
            if (item.ItemType == ItemType.Category)
            {
                ulong? parent_id = category.Parent_Id;

                while (parent_id != null)
                {
                    // Recursion is a nono
                    if (parent_id == item.Id)
                    {
                        ctx.Response.StatusCode = 400;
                        await ctx.Response.WriteAsync("Operation would result in recursion.");
                        return;
                    }

                    parent_id = (await db.PlanetCategories.FindAsync(parent_id)).Parent_Id;
                }
            }

            item.Parent_Id = category.Id;
            item.Position = in_item.Position;

            db.Update(item);
            await db.SaveChangesAsync();

            item.NotifyClientsChange();

            ctx.Response.StatusCode = 200;
            await ctx.Response.WriteAsync("Success");
            return;
        }
    }
}
