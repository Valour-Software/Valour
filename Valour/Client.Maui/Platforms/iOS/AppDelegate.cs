using Foundation;
using UIKit;

namespace Valour.Client.Maui;

[Register("AppDelegate")]
public class AppDelegate : MauiUIApplicationDelegate
{
    protected override MauiApp CreateMauiApp() => MauiProgram.CreateMauiApp();

    public override bool FinishedLaunching(UIApplication application, NSDictionary launchOptions)
    {
        var result = base.FinishedLaunching(application, launchOptions);

        // Creating the billing service starts observing StoreKit, which Apple
        // asks apps to do at launch so interrupted purchases are delivered.
        IPlatformApplication.Current?.Services.GetService<Valour.Client.Device.IStoreBillingService>();

        return result;
    }

    public override void DidEnterBackground(UIApplication application)
    {
        base.DidEnterBackground(application);
        AppLifecycle.NotifyBackground();
    }

    public override void OnActivated(UIApplication application)
    {
        base.OnActivated(application);
        AppLifecycle.NotifyResumed();
    }
}