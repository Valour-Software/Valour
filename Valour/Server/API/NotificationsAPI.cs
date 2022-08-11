using Microsoft.AspNetCore.Mvc;
using Valour.Server.Database;
using Valour.Server.Database.Items;
using Valour.Server.Database.Items.Authorization;
using Valour.Server.Database.Items.Notifications;
using Valour.Server.Database.Items.Users;

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

    public static async Task<IResult> Subscribe(ValourDB db, [FromBody] NotificationSubscription subscription, [FromHeader] string authorization)
    {
        if (string.IsNullOrWhiteSpace(authorization))
            return ValourResult.NoToken();

        var auth = await AuthToken.TryAuthorize(authorization, db);

        if (auth is null)
            return ValourResult.InvalidToken();

        // Force subscription to use auth token's user id
        subscription.UserId = auth.UserId;

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

        await db.NotificationSubscriptions.AddAsync(subscription);
        await db.SaveChangesAsync();

        return Results.Ok("Subscription was accepted.");
    }

    public static async Task<IResult> Unsubscribe(ValourDB db, [FromBody] NotificationSubscription subscription, [FromHeader] string authorization)
    {
        if (string.IsNullOrWhiteSpace(authorization))
            return ValourResult.NoToken();

        var auth = await AuthToken.TryAuthorize(authorization, db);

        if (auth is null)
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
