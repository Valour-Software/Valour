using Foundation;
using UIKit;

namespace Valour.Client.Maui;

[Register("AppDelegate")]
public class AppDelegate : MauiUIApplicationDelegate
{
    protected override MauiApp CreateMauiApp() => MauiProgram.CreateMauiApp();

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