using Microsoft.AspNetCore.Components.WebView.Maui;
using Microsoft.Extensions.Logging;
using Microsoft.Maui.LifecycleEvents;
using Plugin.Firebase.CloudMessaging;
#if ANDROID
using Plugin.Firebase.Core.Platforms.Android;
#endif
using Valour.Client;
using Valour.Client.Maui.Notifications;
using Valour.Client.Maui.Storage;
using Valour.Client.Notifications;
using Valour.Client.Storage;

namespace Valour.Client.Maui;

public static class MauiProgram
{
    public static MauiApp CreateMauiApp()
    {
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
            options.Dsn = "https://4d0d4a0caff54b8d5a0dc1a6e2a17486@o4510867505479680.ingest.us.sentry.io/4510887562444800";
            options.MinimumEventLevel = LogLevel.Error;
            options.SetBeforeSend((e, _) => SentryGate.IsEnabled ? e : null);
        });

        builder.Services.AddMauiBlazorWebView();

#if ANDROID
        BlazorWebViewHandler.BlazorWebViewMapper.AppendToMapping("WebViewAudioConfig", (handler, _) =>
        {
            var webView = handler.PlatformView;
            webView.Settings.MediaPlaybackRequiresUserGesture = false;
            webView.SetWebChromeClient(new AudioPermissionChromeClient());
        });
#endif
        builder.Services.AddSingleton<IAppStorage, MauiStorageService>();
        builder.Services.AddSingleton<IPushNotificationService, MauiPushNotificationService>();
        builder.Services.AddValourClientServices("https://app.valour.gg");

#if DEBUG
        builder.Services.AddBlazorWebViewDeveloperTools();
        builder.Logging.AddDebug();
#endif

        return builder.Build();
    }
}
