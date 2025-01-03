using Valour.Server.Exceptions;

namespace Valour.Server.EndpointFilters;

public class NotHostedExceptionFilter : IEndpointFilter
{
    public async ValueTask<object> InvokeAsync(EndpointFilterInvocationContext ctx, EndpointFilterDelegate next)
    {
        try
        {
            return await next(ctx);
        }
        catch (PlanetNotHostedException e)
        {
            return ValourResult.WrongNode(e.CorrectNode, e.PlanetId);
        }
    }
}