using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.OS;
using Android.Views;
using AndroidX.Activity;
using AndroidX.Core.View;
using Plugin.Firebase.CloudMessaging;
using Plugin.Firebase.CloudMessaging.EventArgs;
using Valour.Client;
using Valour.Client.Device;

namespace Valour.Client.Maui;

// SingleTop ensures a notification tap reuses the running activity (delivering the
// payload to OnNewIntent) instead of stacking a second instance.
[Activity(Theme = "@style/Maui.SplashTheme", MainLauncher = true, LaunchMode = LaunchMode.SingleTop,
    ConfigurationChanges = ConfigChanges.ScreenSize | ConfigChanges.Orientation | ConfigChanges.UiMode |
                           ConfigChanges.ScreenLayout | ConfigChanges.SmallestScreenSize | ConfigChanges.Density)]
public class MainActivity : MauiAppCompatActivity
{
    private static bool _notificationTapHooked;

    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);

        CreateNotificationChannel();
        _ = RequestNotificationPermissionAsync();

        HookNotificationTaps();

        // Hand the launch intent to the FCM plugin so a cold-start notification
        // tap fires NotificationTapped once Firebase is initialized.
        FirebaseCloudMessagingImplementation.OnNewIntent(Intent);

        // Back should dismiss the topmost modal/menu/sidebar in the app,
        // and never destroy the activity - just background it when there
        // is nothing left to close.
        OnBackPressedDispatcher.AddCallback(this, new ValourBackPressedCallback(this));

        AppLifecycle.CallStarted += CallForegroundService.Start;
        AppLifecycle.CallEnded += CallForegroundService.Stop;

        if (Window is null)
            return;

        // The window background shows through transparent system bars
        Window.DecorView.SetBackgroundColor(Android.Graphics.Color.Black);

        // Pad content away from system bars so they never overlap the app UI
        var content = FindViewById(Android.Resource.Id.Content);
        if (content is not null)
        {
            content.SetBackgroundColor(Android.Graphics.Color.Black);
            ViewCompat.SetOnApplyWindowInsetsListener(content, new SystemBarsPaddingListener());
        }
    }

    protected override void OnNewIntent(Intent? intent)
    {
        base.OnNewIntent(intent);

        // Foreground/background notification taps arrive here. Forwarding to the
        // plugin lets it resolve the payload and fire NotificationTapped.
        FirebaseCloudMessagingImplementation.OnNewIntent(intent);
    }

    protected override void OnResume()
    {
        base.OnResume();
        AppLifecycle.NotifyResumed();
    }

    private static void HookNotificationTaps()
    {
        // Survives activity recreation within a process - subscribe only once.
        if (_notificationTapHooked)
            return;

        _notificationTapHooked = true;
        CrossFirebaseCloudMessaging.Current.NotificationTapped += OnNotificationTapped;
    }

    private static void OnNotificationTapped(object? sender, FCMNotificationTappedEventArgs e)
    {
        // The server packs the in-app route under the "url" data key.
        if (e?.Notification?.Data is not null &&
            e.Notification.Data.TryGetValue("url", out var url) &&
            !string.IsNullOrWhiteSpace(url))
        {
            DeepLinkBridge.Open(url);
        }
    }

    protected override void OnPause()
    {
        base.OnPause();
        AppLifecycle.NotifyBackground();
    }

    protected override void OnActivityResult(int requestCode, Result resultCode, Intent? data)
    {
        if (requestCode == AudioPermissionChromeClient.FileChooserRequestCode)
        {
            AudioPermissionChromeClient.HandleFileChooserResult(resultCode, data);
            return;
        }

        base.OnActivityResult(requestCode, resultCode, data);
    }

    private void CreateNotificationChannel()
    {
        if (!OperatingSystem.IsAndroidVersionAtLeast(26))
            return;

        var channel = new NotificationChannel(
            "valour_default",
            "Valour Notifications",
            NotificationImportance.Default)
        {
            Description = "Notifications from Valour"
        };

        var manager = (NotificationManager?)GetSystemService(NotificationService);
        manager?.CreateNotificationChannel(channel);
    }

    private async Task RequestNotificationPermissionAsync()
    {
        if (!OperatingSystem.IsAndroidVersionAtLeast(33))
            return;

        var status = await Permissions.CheckStatusAsync<Permissions.PostNotifications>();
        if (status == PermissionStatus.Granted)
            return;

        await Permissions.RequestAsync<Permissions.PostNotifications>();
    }

    private async Task HandleBackPressedAsync()
    {
        bool handled;
        try
        {
            handled = await BackNavigationService.HandleBackAsync();
        }
        catch (Exception)
        {
            handled = false;
        }

        if (!handled)
        {
            MoveTaskToBack(true);
        }
    }

    private sealed class ValourBackPressedCallback : OnBackPressedCallback
    {
        private readonly MainActivity _activity;

        public ValourBackPressedCallback(MainActivity activity) : base(true)
        {
            _activity = activity;
        }

        public override void HandleOnBackPressed()
        {
            _ = _activity.HandleBackPressedAsync();
        }
    }

    private class SystemBarsPaddingListener : Java.Lang.Object, IOnApplyWindowInsetsListener
    {
        public WindowInsetsCompat OnApplyWindowInsets(Android.Views.View? view, WindowInsetsCompat? insets)
        {
            if (view is null)
                return insets ?? CreateEmptyInsets();

            if (insets is null)
                return CreateEmptyInsets();

            view.SetBackgroundColor(Android.Graphics.Color.Black);

            var bars = insets.GetInsets(WindowInsetsCompat.Type.SystemBars());
            if (bars is null)
                return insets;

            view.SetPadding(bars.Left, bars.Top, bars.Right, bars.Bottom);

            // Force white icons on the black bars — done here because
            // MAUI overrides it when set during lifecycle callbacks
            var window = (view.Context as Activity)?.Window;
            if (window is not null)
            {
                var controller = WindowCompat.GetInsetsController(window, window.DecorView);
                if (controller is not null)
                {
                    controller.AppearanceLightStatusBars = false;
                    controller.AppearanceLightNavigationBars = false;
                }
            }

            return insets;
        }

        private static WindowInsetsCompat CreateEmptyInsets() =>
            new WindowInsetsCompat.Builder().Build()
            ?? throw new InvalidOperationException("Unable to create empty window insets.");
    }
}
