#if IOS || MACCATALYST
using Foundation;
using UIKit;
using UserNotifications;
using Valour.Client.Utility;
using Valour.Sdk.Models;
using Valour.Sdk.Services;
using Valour.Shared.Models;

namespace Valour.Client.Maui.Notifications;

/// <summary>
/// Shows Valour notifications through the system notification center while
/// the app is running, and keeps the app icon badge at the number of unread
/// mentions, replies, and direct messages. Notifications arrive over the
/// app's live connection, so nothing is shown after the app quits.
/// </summary>
public sealed class AppleNotificationService : IDisposable
{
    private const string UrlKey = "url";
    private static readonly TimeSpan TextTimeout = TimeSpan.FromSeconds(5);

    private readonly NotificationService _notificationService;
    private readonly CenterDelegate _centerDelegate = new();
    private bool _enabled;

    public AppleNotificationService(NotificationService notificationService)
    {
        _notificationService = notificationService;

        UNUserNotificationCenter.Current.Delegate = _centerDelegate;

        // The badge follows unread notifications whether or not banners are on.
        _notificationService.NotificationReceived += OnNotificationChanged;
        _notificationService.NotificationsCleared += UpdateBadge;
        _notificationService.UnreadNotificationsLoaded += UpdateBadge;

        if (Preferences.Get("push_subscribed", false))
            Enable();
    }

    public void Enable() => _enabled = true;

    public void Disable()
    {
        _enabled = false;
        UNUserNotificationCenter.Current.RemoveAllDeliveredNotifications();
    }

    public async Task<bool> RequestPermissionAsync()
    {
        var (granted, _) = await UNUserNotificationCenter.Current.RequestAuthorizationAsync(
            UNAuthorizationOptions.Alert | UNAuthorizationOptions.Badge | UNAuthorizationOptions.Sound);
        UpdateBadge();
        return granted;
    }

    public async Task<string> GetPermissionStateAsync()
    {
        var settings = await UNUserNotificationCenter.Current.GetNotificationSettingsAsync();
        return settings.AuthorizationStatus switch
        {
            UNAuthorizationStatus.Authorized or UNAuthorizationStatus.Provisional or UNAuthorizationStatus.Ephemeral => "granted",
            UNAuthorizationStatus.Denied => "denied",
            _ => "default",
        };
    }

    public void Dismiss(Guid notificationId) =>
        UNUserNotificationCenter.Current.RemoveDeliveredNotifications([notificationId.ToString()]);

    public void DismissAll() => UNUserNotificationCenter.Current.RemoveAllDeliveredNotifications();

    private async Task OnNotificationChanged(Notification notification)
    {
        UpdateBadge();

        // Read notifications are removed through IPushNotificationService.
        if (notification.TimeRead is not null || !_enabled || NotificationDisplayGate.ShouldSuppressLocalNotification(notification))
            return;

        // Message text is end-to-end encrypted, so the body the server sent
        // is a placeholder. The app decrypts the message to show its text.
        var id = notification.Id;
        var title = notification.Title ?? "Valour";
        var body = notification.Body;
        var clickUrl = notification.ClickUrl;
        var threadId = notification.ChannelId?.ToString() ?? "valour";
        try
        {
            body = await _notificationService.FetchNotificationTextAsync(notification)
                .WaitAsync(TextTimeout) ?? body;
        }
        catch (Exception)
        {
            // The placeholder is shown when the message cannot be loaded in time.
        }

        // It may have been read elsewhere while the message loaded.
        if (notification.TimeRead is not null)
            return;

        try
        {
            var content = new UNMutableNotificationContent
            {
                Title = title,
                Body = body ?? string.Empty,
                ThreadIdentifier = threadId,
            };

            if (!string.IsNullOrWhiteSpace(clickUrl))
                content.UserInfo = NSDictionary.FromObjectAndKey(new NSString(clickUrl), new NSString(UrlKey));

            var request = UNNotificationRequest.FromIdentifier(id.ToString(), content, null);
            await UNUserNotificationCenter.Current.AddNotificationRequestAsync(request);
        }
        catch
        {
            // Best-effort. A failed notification must not affect the app.
        }
    }

    private void UpdateBadge()
    {
        // Channel activity stays out of the badge, matching the in-app counts.
        var count = _notificationService.GetUnreadInternal()
            .Count(x => x.Source != NotificationSource.ChannelActivity);
        UNUserNotificationCenter.Current.SetBadgeCount(count, null);
    }

    public void Dispose()
    {
        _notificationService.NotificationReceived -= OnNotificationChanged;
        _notificationService.NotificationsCleared -= UpdateBadge;
        _notificationService.UnreadNotificationsLoaded -= UpdateBadge;
    }

    private sealed class CenterDelegate : UNUserNotificationCenterDelegate
    {
        public override void WillPresentNotification(UNUserNotificationCenter center, UNNotification notification,
            Action<UNNotificationPresentationOptions> completionHandler)
        {
            // While the app is in front it shows its own popup and plays its
            // own sound, so the system copy only goes to Notification Center.
            completionHandler(UIApplication.SharedApplication.ApplicationState == UIApplicationState.Active
                ? UNNotificationPresentationOptions.List
                : UNNotificationPresentationOptions.Banner | UNNotificationPresentationOptions.List);
        }

        public override void DidReceiveNotificationResponse(UNUserNotificationCenter center,
            UNNotificationResponse response, Action completionHandler)
        {
            if (response.Notification.Request.Content.UserInfo[UrlKey] is NSString url)
                DeepLinkBridge.Open(url.ToString());

            completionHandler();
        }
    }
}
#endif
