using Valour.Server.Services;

namespace Valour.Server.EndpointFilters;

public class TokenRequiredFilter : IEndpointFilter
{
    private readonly TokenService _tokenService;

    public TokenRequiredFilter(TokenService tokenService)
    {
        _tokenService = tokenService;
    }

    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext ctx, EndpointFilterDelegate next)
    {
        var authToken = await _tokenService.GetCurrentToken();

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
