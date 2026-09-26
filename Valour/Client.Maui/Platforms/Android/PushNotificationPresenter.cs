using Android.App;
using Android.Content;
using Android.Graphics;
using Plugin.Firebase.CloudMessaging;
using Plugin.Firebase.CloudMessaging.Platforms.Android.Extensions;
using Valour.Client.Maui.Storage;
using Valour.Sdk.E2ee;
using Application = Android.App.Application;

namespace Valour.Client.Maui;

/// <summary>
/// Shows push notifications. The server sends data messages, which Android
/// hands to the app even while it is closed, so the message text can be
/// decrypted here with a key the app kept (see <see cref="SecureNotificationKeyStore"/>)
/// before the notification is shown. When the text cannot be decrypted, the
/// notification shows the placeholder the server sent.
/// </summary>
public static class PushNotificationPresenter
{
    public const string ChannelId = "valour_default";
    private const string TagPrefix = "valour-";
    private static readonly TimeSpan IconTimeout = TimeSpan.FromSeconds(3);
    private static readonly HttpClient IconClient = new() { Timeout = IconTimeout };

    /// <summary>
    /// Called by the FCM plugin on its background thread for every message
    /// that reaches the app.
    /// </summary>
    public static void Show(FCMNotification notification)
    {
        try
        {
            // The app shows its own notices while it is open.
            if (IsAppInForeground())
                return;

            var context = Application.Context;
            var data = notification.Data ?? new Dictionary<string, string>();
            var title = Field(data, "title") ?? notification.Title;
            var body = DecryptedText(data) ?? Field(data, "message") ?? notification.Body;
            var tag = TagFor(Field(data, "notificationId"), Field(data, "sourceId"));

            EnsureChannel(context);

            var builder = OperatingSystem.IsAndroidVersionAtLeast(26)
                ? new Notification.Builder(context, ChannelId)
                : new Notification.Builder(context);

            builder
                .SetSmallIcon(Resource.Mipmap.appicon)
                .SetContentTitle(title)
                .SetContentText(body)
                .SetStyle(new Notification.BigTextStyle().BigText(body))
                .SetAutoCancel(true)
                .SetContentIntent(TapIntent(context, notification, tag));

            if (long.TryParse(Field(data, "timeSent"), out var timeSent))
                builder.SetWhen(timeSent).SetShowWhen(true);

            if (LoadIcon(Field(data, "iconUrl") ?? notification.ImageUrl) is { } icon)
                builder.SetLargeIcon(icon);

            var manager = (NotificationManager)context.GetSystemService(Context.NotificationService)!;
            manager.Notify(tag, 0, builder.Build());
        }
        catch (Exception e)
        {
            Console.WriteLine($"[Valour] Could not show a push notification: {e.Message}");
        }
    }

    /// <summary>
    /// Removes the notification for an inbox entry or message, for example
    /// after it was read on another device.
    /// </summary>
    public static void Dismiss(Guid notificationId, long? sourceId)
    {
        var manager = (NotificationManager?)Application.Context.GetSystemService(Context.NotificationService);
        if (manager is null)
            return;

        manager.Cancel(TagFor(notificationId.ToString(), null), 0);
        if (sourceId is not null)
            manager.Cancel(TagFor(null, sourceId.Value.ToString()), 0);
    }

    /// <summary>Removes every push notification the app shows.</summary>
    public static void DismissAll()
    {
        var manager = (NotificationManager?)Application.Context.GetSystemService(Context.NotificationService);
        if (manager is null)
            return;

        foreach (var active in manager.GetActiveNotifications() ?? [])
        {
            if (active.Tag?.StartsWith(TagPrefix, StringComparison.Ordinal) == true)
                manager.Cancel(active.Tag, active.Id);
        }
    }

    public static void EnsureChannel(Context context)
    {
        if (!OperatingSystem.IsAndroidVersionAtLeast(26))
            return;

        var manager = (NotificationManager?)context.GetSystemService(Context.NotificationService);
        if (manager?.GetNotificationChannel(ChannelId) is not null)
            return;

        manager?.CreateNotificationChannel(new NotificationChannel(ChannelId, "Valour Notifications",
            NotificationImportance.Default)
        {
            Description = "Notifications from Valour"
        });
    }

    // Any failure leaves the placeholder, never an empty notification.
    private static string? DecryptedText(IDictionary<string, string> data)
    {
        try
        {
            return TryDecryptText(data);
        }
        catch (Exception e)
        {
            Console.WriteLine($"[Valour] Could not decrypt a push notification: {e.Message}");
            return null;
        }
    }

    private static string? TryDecryptText(IDictionary<string, string> data)
    {
        var envelope = Field(data, "envelope");
        if (envelope is null || !long.TryParse(Field(data, "channelId"), out var channelId))
            return null;

        var planetId = long.TryParse(Field(data, "planetId"), out var parsedPlanetId) ? parsedPlanetId : 0;

        byte[] envelopeBytes;
        try
        {
            envelopeBytes = Convert.FromBase64String(envelope);
        }
        catch (FormatException)
        {
            return null;
        }

        // This runs on the plugin's background thread, which may block.
        var keys = NotificationKeySet.Parse(new SecureNotificationKeyStore().LoadAsync().GetAwaiter().GetResult());
        return NotificationPreviewText.Describe(
            NotificationPreviewDecryptor.TryDecrypt(keys, planetId, channelId, envelopeBytes));
    }

    // A tag per inbox entry, or per message when there is no entry, so one
    // notification never replaces another and each can be dismissed.
    private static string TagFor(string? notificationId, string? sourceId) =>
        !string.IsNullOrEmpty(notificationId) ? $"{TagPrefix}n-{notificationId}"
        : !string.IsNullOrEmpty(sourceId) ? $"{TagPrefix}s-{sourceId}"
        : $"{TagPrefix}{Guid.NewGuid():N}";

    // The plugin reads the notification back from this extra when the
    // activity opens, and raises NotificationTapped with its data.
    private static PendingIntent? TapIntent(Context context, FCMNotification notification, string tag)
    {
        var intent = context.PackageManager?.GetLaunchIntentForPackage(context.PackageName!);
        if (intent is null)
            return null;

        intent.PutExtra(FirebaseCloudMessagingImplementation.IntentKeyFCMNotification, notification.ToBundle());
        intent.SetFlags(ActivityFlags.ClearTop | ActivityFlags.SingleTop);

        // A request code per notification keeps each one's extras.
        return PendingIntent.GetActivity(context, StableHash(tag), intent,
            PendingIntentFlags.Immutable | PendingIntentFlags.UpdateCurrent);
    }

    private static Bitmap? LoadIcon(string? url)
    {
        if (string.IsNullOrEmpty(url) || !Uri.TryCreate(url, UriKind.Absolute, out var uri) ||
            uri.Scheme is not ("https" or "http"))
            return null;

        try
        {
            var bytes = IconClient.GetByteArrayAsync(uri).GetAwaiter().GetResult();
            return BitmapFactory.DecodeByteArray(bytes, 0, bytes.Length);
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static bool IsAppInForeground()
    {
        var state = new ActivityManager.RunningAppProcessInfo();
        ActivityManager.GetMyMemoryState(state);
        return state.Importance == Importance.Foreground;
    }

    private static string? Field(IDictionary<string, string> data, string key) =>
        data.TryGetValue(key, out var value) && !string.IsNullOrEmpty(value) ? value : null;

    private static int StableHash(string value)
    {
        unchecked
        {
            var hash = (int)2166136261;
            foreach (var c in value)
                hash = (hash ^ c) * 16777619;
            return hash;
        }
    }
}
