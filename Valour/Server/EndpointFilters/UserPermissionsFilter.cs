using Valour.Server.Services;
using Valour.Shared.Authorization;

namespace Valour.Server.EndpointFilters;

public class UserPermissionsRequiredFilter : IEndpointFilter
{
    private readonly TokenService _tokenService;
    
    public UserPermissionsRequiredFilter(TokenService tokenService)
    {
        _tokenService = tokenService;
    }
    
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext ctx, EndpointFilterDelegate next)
    {
        var token = await _tokenService.GetCurrentTokenAsync();

        if (token is null)
            return ValourResult.InvalidToken();
        
        var userPermAttr = (UserRequiredAttribute)ctx.HttpContext.Items[nameof(UserRequiredAttribute)];

        foreach (var permEnum in userPermAttr.Permissions)
        {
            var permission = UserPermissions.Permissions[(int)permEnum];
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

public class StaffRequiredFilter : IEndpointFilter
{
    private readonly UserService _userService;
    
    public StaffRequiredFilter(UserService userService)
    {
        _userService = userService;
    }
    
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext ctx, EndpointFilterDelegate next)
    {
        var user = await _userService.GetCurrentUserAsync();
        if (user is null)
            return ValourResult.Forbid("User not found");

        if (!user.ValourStaff)
            return ValourResult.Forbid("This endpoint is staff only");

        return await next(ctx);
    }
}

[AttributeUsage(AttributeTargets.Method, Inherited = false)]
public class StaffRequiredAttribute : Attribute
{

}
