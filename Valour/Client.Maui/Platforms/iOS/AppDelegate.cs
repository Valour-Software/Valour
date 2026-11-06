using Foundation;
using UIKit;

namespace Valour.Client.Maui;

[Register("AppDelegate")]
public class AppDelegate : MauiUIApplicationDelegate
{
    protected override MauiApp CreateMauiApp() => MauiProgram.CreateMauiApp();

    public override bool FinishedLaunching(UIApplication application, NSDictionary launchOptions)
    {
        // Apps built with the iOS 27 SDK must use scenes (see SceneDelegate).
        // UIKit then reports activation per scene instead of calling the app
        // delegate, so the app-wide notifications drive the app lifecycle.
        NSNotificationCenter.DefaultCenter.AddObserver(UIApplication.DidBecomeActiveNotification,
            _ => AppLifecycle.NotifyResumed());
        NSNotificationCenter.DefaultCenter.AddObserver(UIApplication.DidEnterBackgroundNotification,
            _ => AppLifecycle.NotifyBackground());

        var result = base.FinishedLaunching(application, launchOptions);

        // Creating the billing service starts observing StoreKit, which Apple
        // asks apps to do at launch so interrupted purchases are delivered.
        IPlatformApplication.Current?.Services.GetService<Valour.Client.Device.IStoreBillingService>();

        return result;
    }
}
