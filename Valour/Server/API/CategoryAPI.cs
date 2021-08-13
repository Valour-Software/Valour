using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Valour.Server.Categories;
using Valour.Server.Database;
using Valour.Server.Extensions;
using Valour.Server.Oauth;
using Valour.Shared;
using Valour.Shared.Categories;
using Valour.Shared.Oauth;

namespace Valour.Server.API
{
    public static class CategoryAPI
    {
        public static void AddRoutes(WebApplication app)
        {
            app.Map("/api/category/{category_id}", Category);
            app.Map("/api/category/{category_id}/name", Name);
            app.Map("/api/category/{category_id}/parent_id", ParentId);
            app.Map("/api/category/{category_id}/description", Description);
            //app.Map("/api/category/{category_id}/inherits_perms", PermissionsInherit);
        }

        private static async Task Category(HttpContext ctx, ValourDB db, ulong category_id,
                                          [FromHeader] string authorization)
        {
            AuthToken auth = await ServerAuthToken.TryAuthorize(authorization, db);

            if (auth == null)
            {
                ctx.Response.StatusCode = 401;
                await ctx.Response.WriteAsync($"Token is invalid [token: {authorization}]");
                return;
            }

            ServerPlanetCategory category = await db.PlanetCategories.Include(x => x.Planet)
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
                        await ctx.Response.WriteAsJsonAsync((PlanetCategory)category);
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
            AuthToken auth = await ServerAuthToken.TryAuthorize(authorization, db);

            if (auth == null)
            {
                ctx.Response.StatusCode = 401;
                await ctx.Response.WriteAsync($"Token is invalid [token: {authorization}]");
                return;
            }

            ServerPlanetCategory category = await db.PlanetCategories.Include(x => x.Planet)
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

                        TaskResult nameValid = ServerPlanetCategory.ValidateName(body);

                        if (!nameValid.Success)
                        {
                            ctx.Response.StatusCode = 400;
                            await ctx.Response.WriteAsync(nameValid.Message);
                            return;
                        }

                        await category.SetNameAsync(body, db);

                        ctx.Response.StatusCode = 200;
                        await ctx.Response.WriteAsync("Success");
                        return;
                    }
            }
        }

        private static async Task Description(HttpContext ctx, ValourDB db, ulong category_id,
                                      [FromHeader] string authorization)
        {
            AuthToken auth = await ServerAuthToken.TryAuthorize(authorization, db);

            if (auth == null)
            {
                ctx.Response.StatusCode = 401;
                await ctx.Response.WriteAsync($"Token is invalid [token: {authorization}]");
                return;
            }

            ServerPlanetCategory category = await db.PlanetCategories.Include(x => x.Planet)
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

        private static async Task ParentId(HttpContext ctx, ValourDB db, ulong category_id,
                                      [FromHeader] string authorization)
        {
            AuthToken auth = await ServerAuthToken.TryAuthorize(authorization, db);

            if (auth == null)
            {
                ctx.Response.StatusCode = 401;
                await ctx.Response.WriteAsync($"Token is invalid [token: {authorization}]");
                return;
            }

            ServerPlanetCategory category = await db.PlanetCategories.Include(x => x.Planet)
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

                        if (!await category.HasPermission(member, CategoryPermissions.ManageCategory, db))
                        {
                            ctx.Response.StatusCode = 401;
                            await ctx.Response.WriteAsync("Member lacks CategoryPermissions.ManageCategory");
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

                        if (parent.Planet_Id != category.Planet_Id)
                        {
                            ctx.Response.StatusCode = 400;
                            await ctx.Response.WriteAsync($"Category belongs to a different planet");
                            return;
                        }

                        await category.SetParentAsync(parent_id, db);

                        ctx.Response.StatusCode = 200;
                        await ctx.Response.WriteAsync("Success");
                        return;
                    }
            }
        }
    }
}
