using Photino.NET;
using Valour.Client.Components.Notifications;
using Valour.Client.Notifications;
using Valour.Client.Storage;
using Valour.Client.Utility;
using Valour.Sdk.Models;
using Valour.Sdk.Services;

namespace Valour.Client.Photino.Notifications;

/// <summary>
/// Shows desktop notifications (libnotify on Linux) for notifications that
/// arrive over the realtime connection. Like the Windows app, this only works
/// while Valour is running. There is no background push when it is closed.
/// </summary>
public sealed class DesktopNotificationService : IPushNotificationService, IDisposable
{
    private const string EnabledKey = "push_subscribed";
    private static readonly TimeSpan TextTimeout = TimeSpan.FromSeconds(5);

    private readonly PhotinoWindow _window;
    private readonly NotificationService _notificationService;
    private readonly IAppStorage _storage;
    private readonly Lock _lock = new();
    private bool _enabled;

    public DesktopNotificationService(PhotinoWindow window, NotificationService notificationService, IAppStorage storage)
    {
        _window = window;
        _notificationService = notificationService;
        _storage = storage;

        // FileAppStorage completes synchronously.
        if (_storage.GetAsync<bool>(EnabledKey).GetAwaiter().GetResult())
            SetEnabled(true);
    }

    public async Task<PushSubscriptionResult> RequestSubscriptionAsync()
    {
        SetEnabled(true);
        await _storage.SetAsync(EnabledKey, true);
        return Subscribed();
    }

    public async Task UnsubscribeAsync()
    {
        SetEnabled(false);
        await _storage.SetAsync(EnabledKey, false);
    }

    public Task<PushSubscriptionResult> GetSubscriptionAsync() => Task.FromResult(_enabled
        ? Subscribed()
        : new PushSubscriptionResult { Success = false, Error = "Notifications not enabled" });

    public Task<bool> IsNotificationsEnabledAsync() => Task.FromResult(_enabled);

    public Task<string> GetPermissionStateAsync() => Task.FromResult("granted");

    public Task AskForPermissionAsync() => Task.CompletedTask;

    public Task OpenNotificationSettingsAsync() => Task.CompletedTask;

    public Task DismissNotificationAsync(Guid notificationId, long? sourceId) => Task.CompletedTask;

    public Task DismissAllNotificationsAsync() => Task.CompletedTask;

    private static PushSubscriptionResult Subscribed() => new()
    {
        Success = true,
        Subscription = new PushSubscriptionDetails { Endpoint = "desktop-local", Key = "", Auth = "" }
    };

    private void SetEnabled(bool enabled)
    {
        lock (_lock)
        {
            if (_enabled == enabled)
                return;

            _enabled = enabled;
            if (enabled)
                _notificationService.NotificationReceived += OnNotificationReceived;
            else
                _notificationService.NotificationReceived -= OnNotificationReceived;
        }
    }

    private async Task OnNotificationReceived(Notification notification)
    {
        if (notification.TimeRead is not null || NotificationDisplayGate.ShouldSuppressLocalNotification(notification))
            return;

        // Message text is end-to-end encrypted, so the body the server sent
        // is a placeholder. The app decrypts the message to show its text.
        var body = notification.Body;
        try
        {
            body = await _notificationService.FetchNotificationTextAsync(notification).WaitAsync(TextTimeout) ?? body;
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
            _window.SendNotification(notification.Title ?? "Valour", body ?? string.Empty);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Could not show a notification: {ex.Message}");
        }
    }

    public void Dispose() => SetEnabled(false);
}
