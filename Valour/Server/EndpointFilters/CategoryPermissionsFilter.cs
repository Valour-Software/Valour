using Valour.Server.Database;
using Valour.Server.Database.Items.Channels.Planets;
using Valour.Server.Services;
using Valour.Shared.Authorization;

namespace Valour.Server.EndpointFilters;

public class CategoryPermissionsFilter : IEndpointFilter
{
    private readonly ValourDB _db;
    private readonly PermissionsService _permService;

    public CategoryPermissionsFilter(ValourDB db, PermissionsService permService)
    {
        _db = db;
        _permService = permService;
    }
    
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext ctx, EndpointFilterDelegate next)
    {
        var member = ctx.HttpContext.GetMember();
        if (member is null)
            throw new Exception("CategoryChannelPermsRequired attribute requires a PlanetMembershipRequired attribute.");

        var catPermAttr = (CategoryChannelPermsRequiredAttribute)ctx.HttpContext.Items[nameof(CategoryChannelPermsRequiredAttribute)];
        
        var routeName = catPermAttr.categoryRouteName;
        if (!ctx.HttpContext.Request.RouteValues.ContainsKey(routeName))
            throw new Exception($"Could not bind route value for '{routeName}'");

        var categoryId = long.Parse((string)ctx.HttpContext.Request.RouteValues[routeName]);

        var category = await _db.PlanetCategoryChannels.FindAsync(categoryId);

        if (category is null)
            return ValourResult.NotFound<PlanetCategoryChannel>();

        foreach (var permEnum in catPermAttr.permissions)
        {
            var perm = CategoryPermissions.Permissions[(int)permEnum];
            if (!await category.HasPermissionAsync(member, perm, _permService))
                return ValourResult.LacksPermission(perm);
        }

        ctx.HttpContext.Items.Add(categoryId, category);

        return await next(ctx);
    }
}

[AttributeUsage(AttributeTargets.Method, AllowMultiple = true, Inherited = false)]
public class CategoryChannelPermsRequiredAttribute : Attribute
{
    public readonly CategoryPermissionsEnum[] permissions;
    public readonly string categoryRouteName;

    public CategoryChannelPermsRequiredAttribute(string categoryRouteName, params CategoryPermissionsEnum[] permissions)
    {
        this.permissions = permissions;
        this.categoryRouteName = categoryRouteName;
    }

    public CategoryChannelPermsRequiredAttribute(params CategoryPermissionsEnum[] permissions) : this("id", permissions) { }
}
