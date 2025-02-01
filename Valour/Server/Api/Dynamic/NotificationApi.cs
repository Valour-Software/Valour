using System.Web.Http;
using Valour.Server.Workers;
using Valour.Shared.Authorization;

namespace Valour.Server.Api.Dynamic;

public class NotificationApi
{
    [ValourRoute(HttpVerbs.Post, "api/notifications/subscribe")]
    [UserRequired]
    public static async Task<IResult> SubscribeAsync(
        [FromBody] PushNotificationSubscription subscription,
        PushNotificationWorker pushWorker,
        UserService userService)
    {
        var userId = await userService.GetCurrentUserIdAsync();
        
        if (subscription.UserId != userId)
            return ValourResult.Forbid("You do not have permission to subscribe on behalf of another user");

        await pushWorker.QueueNotificationAction(new PushNotificationSubscribe()
        {
            Subscription = subscription
        });

        return ValourResult.Ok();
    }
    
    [ValourRoute(HttpVerbs.Post, "api/notifications/unsubscribe")]
    [UserRequired]
    public static async Task<IResult> UnsubscribeAsync(
        [FromBody] PushNotificationSubscription subscription,
        PushNotificationWorker pushWorker,
        UserService userService)
    {
        var userId = await userService.GetCurrentUserIdAsync();
        if (subscription.UserId != userId)
            return ValourResult.Forbid("You do not have permission to unsubscribe on behalf of another user");
        
        await pushWorker.QueueNotificationAction(new PushNotificationUnsubscribe()
        {
            Subscription = subscription
        });

        return ValourResult.Ok();
    }
    
    [ValourRoute(HttpVerbs.Get, "api/notifications/self/unread/all")]
    [UserRequired(UserPermissionsEnum.FullControl)] // Notifications could contain anything so we require all permissions
    public static async Task<IResult> GetAllUnreadNotificationsAsync(
        UserService userService,
        NotificationService notificationService)
    {
        var selfId = await userService.GetCurrentUserIdAsync();
        var notifications = await notificationService.GetAllUnreadNotifications(selfId);
        return Results.Json(notifications);
    }

    [ValourRoute(HttpVerbs.Post, "api/notifications/self/{id}/read/{value}")]
    [UserRequired(UserPermissionsEnum.FullControl)]
    public static async Task<IResult> SetNotificationRead(
        long id,
        bool value,
        UserService userService,
        NotificationService notificationService)
    {
        var selfId = await userService.GetCurrentUserIdAsync();
        var notification = await notificationService.GetNotificationAsync(id);
        
        if (notification is null)
            return ValourResult.NotFound("Notification not found");
        
        if (notification.UserId != selfId)
            return ValourResult.Forbid("You do not have permission to modify this notification");
        
        var result = await notificationService.SetNotificationRead(id, value);
        if (!result.Success)
            return ValourResult.BadRequest(result.Message);

        return ValourResult.Ok("Notification updated successfully");
    }

    [ValourRoute(HttpVerbs.Post, "api/notifications/self/clear")]
    [UserRequired(UserPermissionsEnum.FullControl)]
    public static async Task<IResult> ClearAllAsync(
        UserService userService,
        NotificationService notificationService)
    {
        var selfId = await userService.GetCurrentUserIdAsync();
        
        var result = await notificationService.ClearNotificationsForUser(selfId);
        if (!result.Success)
            return ValourResult.BadRequest(result.Message);

        return ValourResult.Ok("Notifications cleared successfully");
    }
}