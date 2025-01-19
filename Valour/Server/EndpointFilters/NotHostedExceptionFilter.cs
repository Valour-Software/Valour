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
            if (e.CorrectNode == HostedPlanetResult.DoesNotExist.CorrectNode)
            {
                return ValourResult.NotFound($"Planet {e.PlanetId} was not found.");
            }
            
            return ValourResult.WrongNode(e.CorrectNode, e.PlanetId);
        }
    }
}