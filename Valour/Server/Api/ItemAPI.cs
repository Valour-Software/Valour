using System.Linq.Expressions;
using Valour.Database.Items;
using Valour.Server.Database;
using Valour.Server.Database.Items;
using Valour.Server.Database.Items.Authorization;
using Valour.Server.Database.Items.Channels.Planets;
using Valour.Server.EndpointFilters;
using Valour.Server.EndpointFilters.Attributes;
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
                    {
                        if (val.baseRoute is null)
                            idRoute = dummy.BaseRoute + val.route;
                        else
                            idRoute = val.baseRoute + val.route;
                    }

                    var baseRoute = dummy.BaseRoute;
                    if (val.baseRoute is not null)
                        baseRoute = val.baseRoute;

                    switch (val.method)
                    {
                        case HttpVerbs.Get:
                            builder = app.MapGet(idRoute, del);
                            break;
                        case HttpVerbs.Post:
                            builder = app.MapPost(baseRoute + val.route, del);
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
                    
                    // Add token validation
                    if (attributes.Any(x => x is TokenRequiredAttribute))
                    {
                        builder.AddEndpointFilter<TokenRequiredFilter>();
                    }

                    // Add user validation

                    foreach (var attr in attributes.Where(x => x is UserPermissionsRequiredAttribute))
                    {
                        /* Adds data */
                        builder.AddEndpointFilter(async (ctx, next) =>
                        {
                            ctx.HttpContext.Items[nameof(UserPermissionsRequiredAttribute)] = (UserPermissionsRequiredAttribute)attr;
                            return await next(ctx);
                        });

                        /* Does filtering */
                        builder.AddEndpointFilter<UserPermissionsRequiredFilter>();
                    }

                    var memberAttr = (PlanetMembershipRequiredAttribute)attributes.FirstOrDefault(x => x is PlanetMembershipRequiredAttribute);
                    if (memberAttr is not null)
                    {
                        /* Adds data */
                        builder.AddEndpointFilter(async (ctx, next) =>
                        {
                            ctx.HttpContext.Items[nameof(PlanetMembershipRequiredAttribute)] = memberAttr;
                            return await next(ctx);
                        });

                        /* Does filtering */
                        builder.AddEndpointFilter<PlanetMembershipRequiredFilter>();
                    }

                    // Category permissions validation

                    foreach (var attr in attributes.Where(x => x is CategoryChannelPermsRequiredAttribute))
                    {
                        /* Adds data */
                        builder.AddEndpointFilter(async (ctx, next) =>
                        {
                            ctx.HttpContext.Items[nameof(CategoryChannelPermsRequiredAttribute)] = (CategoryChannelPermsRequiredAttribute)attr;
                            return await next(ctx);
                        });
                        
                        /* Does filtering */
                        builder.AddEndpointFilter<CategoryPermissionsFilter>();
                    }

                    // Channel permissions validation

                    foreach (var attr in attributes.Where(x => x is ChatChannelPermsRequiredAttribute))
                    {
                        /* Adds data */
                        builder.AddEndpointFilter(async (ctx, next) =>
                        {
                            ctx.HttpContext.Items[nameof(ChatChannelPermsRequiredAttribute)] = (ChatChannelPermsRequiredAttribute)attr;
                            return await next(ctx);
                        });
                        
                        /* Does filtering */
                        builder.AddEndpointFilter<ChatChannelPermissionsFilter>();
                    }

                    // Voice channel permissions validation
                    foreach (var attr in attributes.Where(x => x is VoiceChannelPermsRequiredAttribute))
                    {
                        /* Adds data */
                        builder.AddEndpointFilter(async (ctx, next) =>
                        {
                            ctx.HttpContext.Items[nameof(VoiceChannelPermsRequiredAttribute)] = (VoiceChannelPermsRequiredAttribute)attr;
                            return await next(ctx);
                        });
                        
                        /* Does filtering */
                        builder.AddEndpointFilter<VoiceChannelPermissionsFilter>();
                    }
                }
            }
        }

        return this;
    }
}
