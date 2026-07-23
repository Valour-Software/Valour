using Valour.Shared.Authorization;

namespace Valour.Server.EndpointFilters;

/// <summary>
/// Enforces [UserRequired] and [StaffRequired] for an endpoint in a single pass:
/// token validity, account disabled/staff flags, and token scopes.
/// Configured once at route registration and shared across requests, so it must stay stateless.
/// </summary>
public class UserAccessFilter : IEndpointFilter
{
    private readonly UserPermission[] _permissions;
    private readonly bool _staffRequired;

    public UserAccessFilter(UserPermission[] permissions, bool staffRequired)
    {
        _permissions = permissions;
        _staffRequired = staffRequired;
    }

    public async ValueTask<object> InvokeAsync(EndpointFilterInvocationContext ctx, EndpointFilterDelegate next)
    {
        var services = ctx.HttpContext.RequestServices;

        var token = await services.GetRequiredService<TokenService>().GetCurrentTokenAsync();
        if (token is null)
            return ValourResult.InvalidToken();

        var flags = await services.GetRequiredService<UserService>().GetAccessFlagsAsync(token.UserId);
        if (flags is null || flags.Value.Disabled)
            return ValourResult.Forbid("This account has been disabled.");

        if (_staffRequired && !flags.Value.ValourStaff)
            return ValourResult.Forbid("This endpoint is staff only");

        foreach (var permission in _permissions)
        {
            if (!token.HasScope(permission))
                return ValourResult.LacksPermission(permission);
        }

        return await next(ctx);
    }
}

[AttributeUsage(AttributeTargets.Method, Inherited = false)]
public class UserRequiredAttribute : Attribute
{
    public readonly UserPermissionsEnum[] Permissions;

    public UserRequiredAttribute(params UserPermissionsEnum[] permissions)
    {
        this.Permissions = permissions;
    }
}

[AttributeUsage(AttributeTargets.Method, Inherited = false)]
public class StaffRequiredAttribute : Attribute
{

}
