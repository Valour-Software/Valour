using Valour.Shared.Authorization;

namespace Valour.Server.Api.Dynamic;

public class NotificationApi
{
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
}