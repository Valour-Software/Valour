using Valour.Client.Components.Notifications;

namespace Valour.Client.Notifications;

public interface IPushNotificationService
{
    Task<PushSubscriptionResult> RequestSubscriptionAsync();
    Task UnsubscribeAsync();
    Task<PushSubscriptionResult> GetSubscriptionAsync();
    Task<bool> IsNotificationsEnabledAsync();
    Task<string> GetPermissionStateAsync();
    Task AskForPermissionAsync();
    Task OpenNotificationSettingsAsync();
}
