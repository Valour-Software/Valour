#if WINDOWS
using Microsoft.Toolkit.Uwp.Notifications;
using Valour.Client;
using Valour.Client.Utility;
using Valour.Sdk.Models;
using Valour.Sdk.Services;

namespace Valour.Client.Maui.Notifications;

/// <summary>
/// Listens for in-app notifications via SignalR and shows native Windows toast notifications.
/// Works while the app is running (foreground or minimized to tray).
/// </summary>
public class WindowsToastService : IDisposable
{
    private readonly NotificationService _notificationService;
    private bool _enabled;

    private static bool _activationHooked;

    public WindowsToastService(NotificationService notificationService)
    {
        _notificationService = notificationService;

        HookToastActivation();

        // Auto-enable if user previously opted in
        if (Preferences.Get("push_subscribed", false))
        {
            Enable();
        }
    }

    private static void HookToastActivation()
    {
        // Global handler - subscribe once for the process lifetime.
        if (_activationHooked)
            return;

        _activationHooked = true;
        ToastNotificationManagerCompat.OnActivated += OnToastActivated;
    }

    private static void OnToastActivated(ToastNotificationActivatedEventArgsCompat e)
    {
        // Tapping the toast routes to the same in-app destination as the web app.
        var args = ToastArguments.Parse(e.Argument);
        if (args.TryGetValue("url", out var url) && !string.IsNullOrWhiteSpace(url))
        {
            DeepLinkBridge.Open(url);
        }
    }

    public void Enable()
    {
        if (_enabled) return;
        _enabled = true;
        _notificationService.NotificationReceived += OnNotificationReceived;
    }

    public void Disable()
    {
        if (!_enabled) return;
        _enabled = false;
        _notificationService.NotificationReceived -= OnNotificationReceived;
    }

    private void OnNotificationReceived(Notification notification)
    {
        if (notification.TimeRead is not null)
            return;

        if (NotificationDisplayGate.ShouldSuppressLocalNotification(notification))
            return;

        try
        {
            var builder = new ToastContentBuilder()
                .AddText(notification.Title ?? "Valour")
                .AddText(notification.Body ?? string.Empty)
                .AddAudio(new ToastAudio { Silent = true });

            // Carries the in-app route so OnToastActivated can deep link on click.
            if (!string.IsNullOrWhiteSpace(notification.ClickUrl))
            {
                builder.AddArgument("url", notification.ClickUrl);
            }

            if (!string.IsNullOrEmpty(notification.ImageUrl))
            {
                builder.AddAppLogoOverride(new Uri(notification.ImageUrl));
            }

            builder.Show();
        }
        catch
        {
            // Best-effort — don't crash the app over a failed toast
        }
    }

    public void Dispose()
    {
        Disable();
    }
}
#endif
