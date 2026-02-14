using Android.App;
using Android.Content.PM;
using Android.OS;
using Android.Views;
using AndroidX.Core.View;

namespace Valour.Client.Maui;

[Activity(Theme = "@style/Maui.SplashTheme", MainLauncher = true,
    ConfigurationChanges = ConfigChanges.ScreenSize | ConfigChanges.Orientation | ConfigChanges.UiMode |
                           ConfigChanges.ScreenLayout | ConfigChanges.SmallestScreenSize | ConfigChanges.Density)]
public class MainActivity : MauiAppCompatActivity
{
    protected override void OnCreate(Bundle savedInstanceState)
    {
        base.OnCreate(savedInstanceState);

        CreateNotificationChannel();
        _ = RequestNotificationPermissionAsync();
        _ = RequestMicrophonePermissionAsync();

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

    protected override void OnResume()
    {
        base.OnResume();
        AppLifecycle.NotifyResumed();
    }

    private void CreateNotificationChannel()
    {
        if (Build.VERSION.SdkInt < BuildVersionCodes.O)
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

    private async Task RequestMicrophonePermissionAsync()
    {
        if (!OperatingSystem.IsAndroidVersionAtLeast(23))
            return;

        var status = await Permissions.CheckStatusAsync<Permissions.Microphone>();
        if (status == PermissionStatus.Granted)
            return;

        await Permissions.RequestAsync<Permissions.Microphone>();
    }

    private class SystemBarsPaddingListener : Java.Lang.Object, IOnApplyWindowInsetsListener
    {
        public WindowInsetsCompat OnApplyWindowInsets(Android.Views.View? view, WindowInsetsCompat? insets)
        {
            if (view is null || insets is null)
                return insets ?? new WindowInsetsCompat.Builder().Build();

            view.SetBackgroundColor(Android.Graphics.Color.Black);

            var bars = insets.GetInsets(WindowInsetsCompat.Type.SystemBars());
            view.SetPadding(bars.Left, bars.Top, bars.Right, bars.Bottom);

            // Force white icons on the black bars — done here because
            // MAUI overrides it when set during lifecycle callbacks
            var window = (view.Context as Activity)?.Window;
            if (window is not null)
            {
                var controller = WindowCompat.GetInsetsController(window, window.DecorView);
                controller.AppearanceLightStatusBars = false;
                controller.AppearanceLightNavigationBars = false;
            }

            return insets;
        }
    }
}
