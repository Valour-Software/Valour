namespace Valour.Server.Api.Dynamic
{
    public class PlanetChannelApi
    {
        [ValourRoute(HttpVerbs.Get, "api/channels/{id}/nodes")]
        [UserRequired]
        public static async Task<IResult> GetNodesRouteAsync(long id, PlanetChannelService service)
        {
            return Results.Json(await service.GetPermNodesAsync(id));
        }
    }
}
