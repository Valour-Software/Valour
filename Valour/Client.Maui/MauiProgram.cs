using Microsoft.AspNetCore.Components.WebView.Maui;
using Microsoft.Extensions.Logging;
using Microsoft.Maui.LifecycleEvents;
using Microsoft.Maui.Storage;
using System.Text.Json;
#if ANDROID
using Plugin.Firebase.CloudMessaging;
using Plugin.Firebase.Core.Platforms.Android;
#endif
using Valour.Client;
using Valour.Client.Device;
using Valour.Client.Maui.Notifications;
using Valour.Client.Maui.Storage;
using Valour.Client.Notifications;
using Valour.Client.Storage;

namespace Valour.Client.Maui;

public static class MauiProgram
{
    public static MauiApp CreateMauiApp()
    {
        SentryGate.IsEnabled = ReadLocalErrorReportingPreference();

        var builder = MauiApp.CreateBuilder();
        builder
            .UseMauiApp<App>()
            .ConfigureFonts(fonts => { fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular"); });

        builder.ConfigureLifecycleEvents(events =>
        {
#if ANDROID
            events.AddAndroid(android => android.OnCreate((activity, _) =>
                CrossFirebase.Initialize(activity)));
#endif
        });

        builder.UseSentry(options =>
        {
#if WINDOWS
            options.Dsn = "https://aeef5244054b87165a2ecd26ca7ea24e@o4510867505479680.ingest.us.sentry.io/4510927093301248";
#else
            options.Dsn = "https://4d0d4a0caff54b8d5a0dc1a6e2a17486@o4510867505479680.ingest.us.sentry.io/4510887562444800";
#endif
            options.MinimumEventLevel = LogLevel.Error;
            options.SetBeforeSend((e, _) => SentryGate.IsEnabled ? e : null);
        });

        builder.Services.AddMauiBlazorWebView();

#if ANDROID
        BlazorWebViewHandler.BlazorWebViewMapper.AppendToMapping("WebViewAudioConfig", (handler, _) =>
        {
            var webView = handler.PlatformView;
            webView.Settings.MediaPlaybackRequiresUserGesture = false;
            webView.Settings.SetSupportMultipleWindows(true);
            webView.SetWebChromeClient(new AudioPermissionChromeClient());
        });
#endif

        builder.Services.AddSingleton<IAppStorage, MauiStorageService>();
        builder.Services.AddSingleton<IPushNotificationService, MauiPushNotificationService>();
#if ANDROID
        builder.Services.AddSingleton<INativeUpdateService, AndroidUpdateService>();
#elif WINDOWS
        builder.Services.AddSingleton<INativeUpdateService, WindowsUpdateService>();
#endif
        // Native clients should talk directly to the API host.
        builder.Services.AddValourClientServices("https://api.valour.gg");

        // Override the browser share service with the native OS share sheet
        builder.Services.AddSingleton<Valour.Client.Utility.IShareService, MauiShareService>();
#if WINDOWS
        builder.Services.AddSingleton<WindowsToastService>();
        builder.Services.AddSingleton<INativeWindowService, MauiNativeWindowService>();
#endif

#if DEBUG
        builder.Services.AddBlazorWebViewDeveloperTools();
        builder.Logging.AddDebug();
#endif

        return builder.Build();
    }

    private static bool ReadLocalErrorReportingPreference()
    {
        try
        {
            if (!Preferences.Default.ContainsKey(DevicePreferences.ErrorReportingEnabledStorageKey))
            {
                return false;
            }

            try
            {
                return Preferences.Default.Get(DevicePreferences.ErrorReportingEnabledStorageKey, false);
            }
            catch
            {
                // The value may be stored as JSON text by IAppStorage.
            }

            var rawValue = Preferences.Default.Get<string?>(DevicePreferences.ErrorReportingEnabledStorageKey, null);
            if (string.IsNullOrWhiteSpace(rawValue))
            {
                return false;
            }

            if (bool.TryParse(rawValue, out var parsed))
            {
                return parsed;
            }

            return JsonSerializer.Deserialize<bool>(rawValue);
        }
        catch
        {
            return false;
        }
    }
}
