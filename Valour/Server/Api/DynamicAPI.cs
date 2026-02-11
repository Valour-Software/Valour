using System.Linq.Expressions;

namespace Valour.Server.API;

/// <summary>
/// The ServerModel API allows for easy construction of routes
/// relating to Valour Items.
/// </summary>
public class DynamicAPI<T> where T : class
{
    /// <summary>
    /// This method registers the API routes and should only be called
    /// once during the application runtime.
    /// </summary>
    public DynamicAPI<T> RegisterRoutes(WebApplication app)
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
                    var delegateType = Expression.GetDelegateType(paramTypes.Append(method.ReturnType).ToArray());
                    var del = method.CreateDelegate(delegateType);

                    RouteHandlerBuilder builder = null;
                    
                    switch (val.Method)
                    {
                        case HttpVerbs.Get:
                            builder = app.MapGet(val.Route, del);
                            break;
                        case HttpVerbs.Post:
                            builder = app.MapPost(val.Route, del);
                            break;
                        case HttpVerbs.Put:
                            builder = app.MapPut(val.Route, del);
                            break;
                        case HttpVerbs.Patch:
                            builder = app.MapPatch(val.Route, del);
                            break;
                        case HttpVerbs.Delete:
                            builder = app.MapDelete(val.Route, del);
                            break;
                    }
                    
                    // Add user validation

                    foreach (var attr in attributes.Where(x => x is UserRequiredAttribute))
                    {
                        /* Adds data */
                        builder.AddEndpointFilter(async (ctx, next) =>
                        {
                            ctx.HttpContext.Items[nameof(UserRequiredAttribute)] = (UserRequiredAttribute)attr;
                            return await next(ctx);
                        });

                        /* Does filtering */
                        builder.AddEndpointFilter<UserPermissionsRequiredFilter>();
                    }
                    
                    // Add staff validation

                    foreach (var attr in attributes.Where(x => x is StaffRequiredAttribute))
                    {
                        /* Does filtering */
                        builder.AddEndpointFilter<StaffRequiredFilter>();
                    }
                    
                    // Add wrong node exception handling
                    builder.AddEndpointFilter<NotHostedExceptionFilter>();
                }
            }
        }

        return this;
    }
}
