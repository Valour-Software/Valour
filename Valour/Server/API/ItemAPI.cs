using System.Linq.Expressions;
using Valour.Server.Database;
using Valour.Server.Database.Items;
using Valour.Server.Database.Items.Authorization;
using Valour.Server.Database.Items.Planets.Channels;
using Valour.Shared.Authorization;

namespace Valour.Server.API;

/// <summary>
/// The Item API allows for easy construction of routes
/// relating to Valour Items.
/// </summary>
public class ItemAPI<T> where T : Item
{
    /// <summary>
    /// This method registers the API routes and should only be called
    /// once during the application runtime.
    /// </summary>
    public ItemAPI<T> RegisterRoutes(WebApplication app)
    {
        T dummy = (T)Activator.CreateInstance(typeof(T));

        // Custom routes
        var methods = typeof(T).GetMethods();
        foreach (var method in methods)
        {
            var attributes = method.GetCustomAttributes(false);

            foreach (var att in attributes)
            {
                if (att is ValourRouteAttribute)
                {
                    if (!method.IsStatic)
                        throw new Exception($"Cannot use a non-static method for ValourRoute! Class: {typeof(T).Name}, Method: {method.Name}");

                    var val = (ValourRouteAttribute)att;

                    // This magically builds a delegate matching the method
                    var paramTypes = method.GetParameters().Select(x => x.ParameterType);
                    Type delegateType = Expression.GetDelegateType(paramTypes.Append(method.ReturnType).ToArray());
                    var del = method.CreateDelegate(delegateType);

                    RouteHandlerBuilder builder = null;

                    var idRoute = dummy.IdRoute;
                    if (val.route != null)
                        idRoute = dummy.BaseRoute + val.route;

                    switch (val.method)
                    {
                        case HttpVerbs.Get:
                            builder = app.MapGet(idRoute, del);
                            break;
                        case HttpVerbs.Post:
                            builder = app.MapPost(dummy.BaseRoute + val.route, del);
                            break;
                        case HttpVerbs.Put:
                            builder = app.MapPut(idRoute, del);
                            break;
                        case HttpVerbs.Patch:
                            builder = app.MapPatch(idRoute, del);
                            break;
                        case HttpVerbs.Delete:
                            builder = app.MapDelete(idRoute, del);
                            break;
                    }

                    if (attributes.Any(x => x is InjectDbAttribute))
                    {
                        builder.AddFilter(async (ctx, next) =>
                        {
                            var db = ctx.HttpContext.RequestServices.GetService<ValourDB>();
                            ctx.HttpContext.Items.Add("db", db);
                            return await next(ctx);
                        });
                    }

                    // Add token validation
                    if (attributes.Any(x => x is TokenRequiredAttribute))
                    {
                        builder.AddFilter(async (ctx, next) =>
                        {
                            var hasAuth = ctx.HttpContext.Request.Headers.ContainsKey("authorization");

                            if (!hasAuth)
                                return ValourResult.NoToken();

                            var authKey = ctx.HttpContext.Request.Headers["authorization"];

                            var db = ctx.HttpContext.GetDb();
                            if (db is null)
                                throw new Exception("TokenRequired attribute requires InjectDB attribute");

                            var authToken = await AuthToken.TryAuthorize(authKey, db);

                            if (authToken is null)
                                return ValourResult.InvalidToken();

                            ctx.HttpContext.Items.Add("token", authToken);

                            return await next(ctx);
                        });
                    }

                    // Add user validation

                    foreach (var attr in attributes.Where(x => x is UserPermissionsRequiredAttribute))
                    {
                        var userPermAttr = (UserPermissionsRequiredAttribute)attr;

                        builder.AddFilter(async (ctx, next) =>
                        {
                            var token = ctx.HttpContext.GetToken();
                            if (token is null)
                                throw new Exception("UserPermissionRequired attribute requires a TokenRequired attribute.");

                            foreach (var permEnum in userPermAttr.permissions)
                            {
                                var permission = UserPermissions.Permissions[(int)permEnum];
                                if (!token.HasScope(permission))
                                    return ValourResult.LacksPermission(permission);
                            }

                            return await next(ctx);
                        });
                    }

                    var planetAttr = (PlanetPermsRequiredAttribute)attributes.FirstOrDefault(x => x is PlanetPermsRequiredAttribute);
                    if (planetAttr is not null)
                    {
                        builder.AddFilter(async (ctx, next) =>
                        {
                            var member = ctx.HttpContext.GetMember();
                            if (member is null)
                                throw new Exception("PlanetPermsRequiredAttribute attribute requires a PlanetMembershipRequired attribute.");

                            var routeId = planetAttr.planetRouteName;

                            if (!ctx.HttpContext.Request.RouteValues.ContainsKey(routeId))
                                throw new Exception($"Could not bind route value for '{routeId}'");

                            var routeVal = (ulong)ctx.HttpContext.Request.RouteValues[routeId];

                            var db = ctx.HttpContext.GetDb();
                            if (db is null)
                                throw new Exception("PlanetPermsRequired attribute requires InjectDB attribute");

                            var planet = await db.Planets.FirstOrDefaultAsync(x => x.Id == routeVal);

                            foreach (var permEnum in planetAttr.permissions)
                            {
                                var perm = PlanetPermissions.Permissions[(int)permEnum];
                                if (!await planet.HasPermissionAsync(member, perm, db))
                                    return ValourResult.LacksPermission(perm);
                            }

                            ctx.HttpContext.Items.Add(planet.Id, planet);

                            return await next(ctx);
                        });
                    }

                    var memberAttr = attributes.FirstOrDefault(x => x is PlanetMembershipRequiredAttribute);
                    if (memberAttr is not null)
                    {
                        builder.AddFilter(async (ctx, next) =>
                        {
                            var token = ctx.HttpContext.GetToken();
                            if (token is null)
                                throw new Exception("PlanetMembershipRequired attribute requires a TokenRequired attribute.");

                            var routeId = ((PlanetMembershipRequiredAttribute)memberAttr).planetRouteName;

                            if (!ctx.HttpContext.Request.RouteValues.ContainsKey(routeId))
                                throw new Exception($"Could not bind route value for '{routeId}'");

                            var routeVal = (ulong)ctx.HttpContext.Request.RouteValues[routeId];

                            var db = ctx.HttpContext.GetDb();
                            if (db is null)
                                throw new Exception("PlanetMembershipRequired attribute requires InjectDB attribute");

                            var member = await db.PlanetMembers.FirstOrDefaultAsync(x => x.UserId == token.UserId && x.PlanetId == routeVal);
                            if (member is null)
                                return ValourResult.NotPlanetMember();

                            ctx.HttpContext.Items.Add("member", member);

                            return await next(ctx);
                        });
                    }

                    // Category permissions validation

                    foreach (var attr in attributes.Where(x => x is CategoryChannelPermsRequiredAttribute))
                    {
                        var catPermAttr = (CategoryChannelPermsRequiredAttribute)attr;

                        builder.AddFilter(async (ctx, next) =>
                        {
                            var member = ctx.HttpContext.GetMember();
                            if (member is null)
                                throw new Exception("CategoryChannelPermsRequired attribute requires a PlanetMembershipRequired attribute.");

                            var db = ctx.HttpContext.GetDb();
                            if (db is null)
                                throw new Exception("CategoryChannelPermsRequired attribute requires InjectDB attribute");

                            var routeName = catPermAttr.categoryRouteName;
                            if (!ctx.HttpContext.Request.RouteValues.ContainsKey(routeName))
                                throw new Exception($"Could not bind route value for '{routeName}'");

                            var categoryId = (ulong)ctx.HttpContext.Request.RouteValues[routeName];

                            var category = await db.PlanetCategoryChannels.FindAsync(categoryId);

                            if (category is null)
                                return ValourResult.NotFound<PlanetCategoryChannel>();

                            foreach (var permEnum in catPermAttr.permissions)
                            {
                                var perm = CategoryPermissions.Permissions[(int)permEnum];
                                if (!await category.HasPermissionAsync(member, perm, db))
                                    return ValourResult.LacksPermission(perm);
                            }

                            ctx.HttpContext.Items.Add(categoryId, category);

                            return await next(ctx);
                        });
                    }

                    // Channel permissions validation

                    foreach (var attr in attributes.Where(x => x is ChatChannelPermsRequiredAttribute))
                    {
                        var chanPermAttr = (ChatChannelPermsRequiredAttribute)attr;

                        builder.AddFilter(async (ctx, next) =>
                        {
                            var member = ctx.HttpContext.GetMember();
                            if (member is null)
                                throw new Exception("ChatChannelPermsRequired attribute requires a PlanetMembershipRequired attribute.");

                            var db = ctx.HttpContext.GetDb();
                            if (db is null)
                                throw new Exception("ChatChannelPermsRequired attribute requires InjectDB attribute");

                            var routeName = chanPermAttr.channelRouteName;
                            if (!ctx.HttpContext.Request.RouteValues.ContainsKey(routeName))
                                throw new Exception($"Could not bind route value for '{routeName}'");

                            var channelId = (ulong)ctx.HttpContext.Request.RouteValues[routeName];

                            var channel = await db.PlanetChatChannels.FindAsync(channelId);

                            if (channel is null)
                                return ValourResult.NotFound<PlanetChatChannel>();

                            foreach (var permEnum in chanPermAttr.permissions)
                            {
                                var perm = ChatChannelPermissions.Permissions[(int)permEnum];
                                if (!await channel.HasPermissionAsync(member, perm, db))
                                    return ValourResult.LacksPermission(perm);
                            }

                            ctx.HttpContext.Items.Add(channelId, channel);

                            return await next(ctx);
                        });
                    }
                }
            }
        }

        return this;
    }
}
