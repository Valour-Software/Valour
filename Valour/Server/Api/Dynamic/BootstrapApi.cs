using Valour.Shared.Authorization;
using Valour.Server.Database;
using Valour.Server.Mapping;
using Microsoft.EntityFrameworkCore;

namespace Valour.Server.Api.Dynamic;

/// <summary>
/// Coalesces the authenticated state every app load needs. This replaces nine
/// cross-origin requests and their corresponding CORS preflights with one.
/// </summary>
// Non-static so it can be used as the DynamicAPI<T> type argument (T : class,
// instantiated via Activator.CreateInstance). The route handler itself stays
// static, as DynamicAPI requires. Making this a static class silently drops the
// route: DynamicAPI<BootstrapApi> won't compile, so the registration gets removed
// and api/bootstrap falls through to the SPA fallback.
public class BootstrapApi
{
    [ValourRoute(HttpVerbs.Get, "api/bootstrap")]
    [UserRequired(UserPermissionsEnum.FullControl)]
    public static async Task<IResult> GetAsync(
        UserService userService,
        UserBlockService userBlockService,
        NotificationService notificationService,
        UnreadService unreadService,
        FederationJoinService federationJoinService,
        EcoService ecoService,
        ValourDb db)
    {
        var userId = await userService.GetCurrentUserIdAsync();

        // These services share a scoped DbContext, so execute sequentially.
        // The win here is eliminating browser/edge round-trips; each query
        // remains independently testable through its original endpoint.
        var friends = await userService.GetFriendsDataAsync(userId);
        var blocks = await userBlockService.GetBlocksAsync(userId);
        var planets = await userService.GetJoinedPlanetInfo(userId);
        var planetIds = planets.Select(x => x.Id).ToList();
        var myPlanetMembers = await db.PlanetMembers
            .AsNoTracking()
            .Where(x => x.UserId == userId && planetIds.Contains(x.PlanetId))
            .Select(x => x.ToModel())
            .ToListAsync();
        var memberships = await federationJoinService.GetMembershipsAsync(userId);
        var gifFavorites = await userService.GetGifFavoritesAsync(userId);
        var globalAccount = await ecoService.GetGlobalAccountAsync(userId);
        var notifications = await notificationService.GetAllUnreadNotifications(userId);
        var unreadPlanets = await unreadService.GetUnreadPlanets(userId);
        var unreadDirectChannels = await unreadService.GetUnreadChannels(null, userId);
        var preferences = (await UserApi.EnsurePreferencesAsync(userId, db)).ToModel();

        return Results.Json(new
        {
            friendUsers = friends.outgoing
                .Concat(friends.incoming)
                .DistinctBy(x => x.Id),
            addedFriendIds = friends.outgoing.Select(x => x.Id),
            addedByFriendIds = friends.incoming.Select(x => x.Id),
            blocks,
            planets,
            myPlanetMembers,
            federatedMemberships = memberships,
            gifFavorites,
            globalAccount,
            unreadNotifications = notifications,
            unreadPlanets,
            unreadDirectChannels,
            preferences
        });
    }
}
