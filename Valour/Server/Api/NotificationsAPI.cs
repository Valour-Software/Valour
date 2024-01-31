using Microsoft.AspNetCore.Mvc;
using Valour.Sdk.Models;
using Valour.Server.Database;

namespace Valour.Server.API;

public class NotificationsAPI : BaseAPI
{
    /// <summary>
    /// Adds the routes for this API section
    /// </summary>
    public static void AddRoutes(WebApplication app)
    {
        app.MapPost("api/notification/subscribe", Subscribe);
        app.MapPost("api/notification/unsubscribe", Unsubscribe);
    }

    public static async Task<IResult> Subscribe(ValourDB db, [FromBody] NotificationSubscription subscription, UserService userService, [FromHeader] string authorization)
    {
        if (string.IsNullOrWhiteSpace(authorization))
            return ValourResult.NoToken();

        var userId = await userService.GetCurrentUserIdAsync();

        if (userId is long.MinValue)
            return ValourResult.InvalidToken();

        // Force subscription to use auth token's user id
        subscription.UserId = userId;

        // Ensure subscription data is there
        if (string.IsNullOrWhiteSpace(subscription.Endpoint)
            || string.IsNullOrWhiteSpace(subscription.Auth)
            || string.IsNullOrWhiteSpace(subscription.Key))
        {
            return ValourResult.BadRequest("Subscription data is incomplete.");
        }

        // Look for old subscription
        var old = await db.NotificationSubscriptions.FirstOrDefaultAsync(x => x.Endpoint == subscription.Endpoint);

        if (old != null)
        {
            if (old.Auth == subscription.Auth && old.Key == subscription.Key)
                return Results.Ok("There is already a subscription for this endpoint.");

            // Update old subscription
            old.Auth = subscription.Auth;
            old.Key = subscription.Key;

            db.NotificationSubscriptions.Update(old);
            await db.SaveChangesAsync();

            return Results.Ok("Updated subscription.");
        }

        subscription.Id = IdManager.Generate();

        await db.NotificationSubscriptions.AddAsync(subscription.ToDatabase());
        await db.SaveChangesAsync();

        return Results.Ok("Subscription was accepted.");
    }

    public static async Task<IResult> Unsubscribe(ValourDB db, [FromBody] NotificationSubscription subscription, UserService userService, [FromHeader] string authorization)
    {
        if (string.IsNullOrWhiteSpace(authorization))
            return ValourResult.NoToken();

        var userId = await userService.GetCurrentUserIdAsync();

        if (userId is long.MinValue)
            return ValourResult.InvalidToken();

        // Look for old subscription
        var old = await db.NotificationSubscriptions.FirstOrDefaultAsync(x => x.Endpoint == subscription.Endpoint);

        if (old is null)
            return Results.Ok("Subscription already removed.");


        db.NotificationSubscriptions.Remove(old);
        await db.SaveChangesAsync();

        return Results.Ok("Removed subscription.");
    }
}
