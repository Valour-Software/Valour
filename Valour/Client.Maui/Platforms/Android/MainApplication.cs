using Android.App;
using Android.Runtime;
using Plugin.Firebase.CloudMessaging;

namespace Valour.Client.Maui;

// Debug builds may talk to a development server on the local network over
// plain HTTP (see ValourApiBase in the project file). Release builds use HTTPS only.
#if DEBUG
[Application(UsesCleartextTraffic = true)]
#else
[Application]
#endif
public class MainApplication : MauiApplication
{
    public MainApplication(IntPtr handle, JniHandleOwnership ownership)
        : base(handle, ownership)
    {
    }

    public override void OnCreate()
    {
        base.OnCreate();

        // A push message can start the app with no activity, so its handler
        // is set up here rather than in MainActivity.
        FirebaseCloudMessagingImplementation.ShowLocalNotificationAction = PushNotificationPresenter.Show;
    }

    protected override MauiApp CreateMauiApp() => MauiProgram.CreateMauiApp();
}