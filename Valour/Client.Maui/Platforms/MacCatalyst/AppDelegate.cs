using Foundation;
using UIKit;
using UserNotifications;

namespace Valour.Client.Maui;

[Register("AppDelegate")]
public class AppDelegate : MauiUIApplicationDelegate, IUNUserNotificationCenterDelegate
{
    protected override MauiApp CreateMauiApp() => MauiProgram.CreateMauiApp();

    public override bool FinishedLaunching(UIApplication application, NSDictionary? launchOptions)
    {
        UNUserNotificationCenter.Current.Delegate = this;
        return base.FinishedLaunching(application, launchOptions);
    }

    [Export("userNotificationCenter:willPresentNotification:withCompletionHandler:")]
    public void WillPresentNotification(
        UNUserNotificationCenter center,
        UNNotification notification,
        Action<UNNotificationPresentationOptions> completionHandler)
    {
        completionHandler(
            UNNotificationPresentationOptions.Banner |
            UNNotificationPresentationOptions.List |
            UNNotificationPresentationOptions.Sound
        );
    }
}
