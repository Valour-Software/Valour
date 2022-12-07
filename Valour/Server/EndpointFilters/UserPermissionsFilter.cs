using Valour.Server.Database;
using Valour.Server.Database.Items.Authorization;
using Valour.Shared.Authorization;

namespace Valour.Server.EndpointFilters;

public class UserPermissionsRequiredFilter : IEndpointFilter
{
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext ctx, EndpointFilterDelegate next)
    {
        var token = ctx.HttpContext.GetToken();
        if (token is null)
            throw new Exception("UserPermissionRequired attribute requires a TokenRequired attribute.");

        var userPermAttr = (UserPermissionsRequiredAttribute)ctx.HttpContext.Items[nameof(UserPermissionsRequiredAttribute)];
        
        foreach (var permEnum in userPermAttr.permissions)
        {
            var permission = UserPermissions.Permissions[(int)permEnum];
            if (!token.HasScope(permission))
                return ValourResult.LacksPermission(permission);
        }

        return await next(ctx);
    }
}

[AttributeUsage(AttributeTargets.Method, Inherited = false)]
public class UserPermissionsRequiredAttribute : Attribute
{
    public readonly UserPermissionsEnum[] permissions;

    public UserPermissionsRequiredAttribute(params UserPermissionsEnum[] permissions)
    {
        this.permissions = permissions;
    }
}
