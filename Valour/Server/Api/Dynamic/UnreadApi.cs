using Valour.Shared.Authorization;

namespace Valour.Server.Api.Dynamic;

public class UnreadApi
{
    [ValourRoute(HttpVerbs.Get, "api/unread/planets")]
    [UserRequired(UserPermissionsEnum.Messages)]
    public static async Task<IResult> GetUnreadPlanetsAsync(
        TokenService tokenService,
        UnreadService unreadService
    )
    {
        var token = await tokenService.GetCurrentTokenAsync();
        if (token is null)
            return ValourResult.InvalidToken();

        var userId = token.UserId;
        var unreadPlanets = await unreadService.GetUnreadPlanets(userId);
        return Results.Json(unreadPlanets);
    }

    [ValourRoute(HttpVerbs.Get, "api/unread/planets/{planetId}/channels")]
    [ValourRoute(HttpVerbs.Get, "api/unread/direct/channels")]
    [UserRequired(UserPermissionsEnum.Messages)]
    public static async Task<IResult> GetUnreadChannelsAsync(
        long? planetId,
        TokenService tokenService,
        UnreadService unreadService
    )
    {
        var token = await tokenService.GetCurrentTokenAsync();
        if (token is null)
            return ValourResult.InvalidToken();

        var userId = token.UserId;
        var unreadChannels = await unreadService.GetUnreadChannels(planetId, userId);
        return Results.Json(unreadChannels);
    } 
}
