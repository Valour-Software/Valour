using Valour.Server.Database;
using Valour.Server.Database.Items.Authorization;

namespace Valour.Server.EndpointFilters;

public class TokenRequiredFilter : IEndpointFilter
{
    private readonly ValourDB _db;

    public TokenRequiredFilter(ValourDB db)
    {
        _db = db;
    }
    
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext ctx, EndpointFilterDelegate next)
    {
        var hasAuth = ctx.HttpContext.Request.Headers.ContainsKey("authorization");

        if (!hasAuth)
            return ValourResult.NoToken();

        var authKey = ctx.HttpContext.Request.Headers["authorization"];
        
        var authToken = await AuthToken.TryAuthorize(authKey, _db);

        if (authToken is null)
            return ValourResult.InvalidToken();

        ctx.HttpContext.Items.Add("token", authToken);

        return await next(ctx);
    }
}

[AttributeUsage(AttributeTargets.Method, Inherited = false)]
public class TokenRequiredAttribute : Attribute
{
}
