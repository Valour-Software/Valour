using System.Linq.Expressions;
using System.Reflection;
using Valour.Shared.Authorization;

namespace Valour.Server.API;

/// <summary>
/// Registers minimal API routes for every [ValourRoute] method in the server assembly.
/// </summary>
public static class DynamicAPI
{
    // Stateless, so a single instance serves every endpoint
    private static readonly NotHostedExceptionFilter NotHostedFilter = new();

    /// <summary>
    /// Scans the server assembly for [ValourRoute] methods and registers their routes.
    /// Should only be called once during startup.
    /// </summary>
    public static void RegisterAll(WebApplication app)
    {
        // Tracks (verb, route) pairs so duplicates fail at startup instead of
        // as an AmbiguousMatchException on the first request to hit them.
        var registered = new HashSet<(HttpVerbs, string)>();

        foreach (var type in typeof(DynamicAPI).Assembly.GetTypes())
        {
            foreach (var method in type.GetMethods(BindingFlags.Public | BindingFlags.Static |
                                                   BindingFlags.Instance | BindingFlags.DeclaredOnly))
            {
                if (!method.IsDefined(typeof(ValourRouteAttribute), false))
                    continue;

                RegisterMethod(app, type, method, registered);
            }
        }
    }

    private static void RegisterMethod(WebApplication app, Type type, MethodInfo method, HashSet<(HttpVerbs, string)> registered)
    {
        if (!method.IsStatic)
            throw new Exception($"Cannot use a non-static method for ValourRoute! Class: {type.Name}, Method: {method.Name}");

        // This magically builds a delegate matching the method
        var paramTypes = method.GetParameters().Select(x => x.ParameterType);
        var delegateType = Expression.GetDelegateType(paramTypes.Append(method.ReturnType).ToArray());
        var del = method.CreateDelegate(delegateType);

        var attributes = method.GetCustomAttributes(false);
        var userRequired = attributes.OfType<UserRequiredAttribute>().FirstOrDefault();
        var staffRequired = attributes.OfType<StaffRequiredAttribute>().Any();

        UserAccessFilter accessFilter = null;
        if (userRequired is not null || staffRequired)
        {
            // Resolve permission scopes once here rather than on every request
            var permissions = userRequired?.Permissions
                .Select(p => UserPermissions.Permissions[(int)p])
                .ToArray() ?? [];

            accessFilter = new UserAccessFilter(permissions, staffRequired);
        }

        foreach (var route in attributes.OfType<ValourRouteAttribute>())
        {
            if (!registered.Add((route.Method, route.Route.ToLowerInvariant())))
                throw new Exception($"Duplicate route: {route.Method} {route.Route} ({type.Name}.{method.Name})");

            var builder = route.Method switch
            {
                HttpVerbs.Get => app.MapGet(route.Route, del),
                HttpVerbs.Post => app.MapPost(route.Route, del),
                HttpVerbs.Put => app.MapPut(route.Route, del),
                HttpVerbs.Patch => app.MapPatch(route.Route, del),
                HttpVerbs.Delete => app.MapDelete(route.Route, del),
                _ => throw new Exception($"Unsupported HTTP verb {route.Method} on {type.Name}.{method.Name}"),
            };

            builder.WithDisplayName($"{type.Name}.{method.Name}");
            builder.WithTags(type.Name);

            if (accessFilter is not null)
                builder.AddEndpointFilter(accessFilter);

            builder.AddEndpointFilter(NotHostedFilter);
        }
    }
}
