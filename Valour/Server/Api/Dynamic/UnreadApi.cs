namespace Valour.Server.Api.Dynamic;

public class UnreadApi
{
    [ValourRoute(HttpVerbs.Get, "api/unread/planets")]
    public static async Task<IResult> GetUnreadPlanetsAsync(
        TokenService tokenService,
        UnreadService unreadService
    )
    {
        var token = await tokenService.GetCurrentTokenAsync();
        var userId = token.UserId;
        var unreadPlanets = await unreadService.GetUnreadPlanets(userId);
        return Results.Json(unreadPlanets);
    }

    [ValourRoute(HttpVerbs.Get, "api/unread/planets/{planetId}/channels")]
    [ValourRoute(HttpVerbs.Get, "api/unread/direct/channels")]
    public static async Task<IResult> GetUnreadChannelsAsync(
        long? planetId,
        TokenService tokenService,
        UnreadService unreadService
    )
    {
        var token = await tokenService.GetCurrentTokenAsync();
        var userId = token.UserId;
        var unreadChannels = await unreadService.GetUnreadChannels(planetId, userId);
        return Results.Json(unreadChannels);
    } 
}