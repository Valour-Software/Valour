using Valour.Server.Database;
using Valour.Shared.Authorization;

namespace Valour.Server.EndpointFilters;

public class PlanetMembershipRequiredFilter : IEndpointFilter
{
    private readonly ValourDB _db;

    public PlanetMembershipRequiredFilter(ValourDB db)
    {
        _db = db;
    }
    
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext ctx, EndpointFilterDelegate next)
    {
        var token = ctx.HttpContext.GetToken();
        if (token is null)
            throw new Exception("PlanetMembershipRequired attribute requires a TokenRequired attribute.");

        var memberAttr =
            (PlanetMembershipRequiredAttribute)ctx.HttpContext.Items[nameof(PlanetMembershipRequiredAttribute)];
        
        var routeId = memberAttr.planetRouteName;

        if (!ctx.HttpContext.Request.RouteValues.ContainsKey(routeId))
            throw new Exception($"Could not bind route value for '{routeId}'");

        var routeVal = long.Parse((string)ctx.HttpContext.Request.RouteValues[routeId]);
        
        var member = await _db.PlanetMembers.Include(x => x.Planet).FirstOrDefaultAsync(x => x.UserId == token.UserId && x.PlanetId == routeVal);
        if (member is null)
            return ValourResult.NotPlanetMember();

        foreach (var permEnum in memberAttr.permissions)
        {
            var perm = PlanetPermissions.Permissions[(int)permEnum];
            if (!await member.Planet.HasPermissionAsync(member, perm, _db))
                return ValourResult.LacksPermission(perm);
        }

        ctx.HttpContext.Items.Add("member", member);
        ctx.HttpContext.Items.Add(routeVal, member.Planet);

        return await next(ctx);
    }
}

[AttributeUsage(AttributeTargets.Method, Inherited = false)]
public class PlanetMembershipRequiredAttribute : Attribute
{
    public readonly string planetRouteName;
    public readonly PlanetPermissionsEnum[] permissions;

    public PlanetMembershipRequiredAttribute(string planetRouteName = "planetId", params PlanetPermissionsEnum[] permissions)
    {
        this.planetRouteName = planetRouteName;
        this.permissions = permissions;
    }

    public PlanetMembershipRequiredAttribute() : this("planetId") { }
}