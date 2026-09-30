using Foundation;
using ObjCRuntime;
using UIKit;

namespace Valour.Client.Maui;

[Register("AppDelegate")]
public class AppDelegate : MauiUIApplicationDelegate
{
    private const string HelpUrl = "https://valour.gg/faq/";

    protected override MauiApp CreateMauiApp() => MauiProgram.CreateMauiApp();

    public override bool FinishedLaunching(UIApplication application, NSDictionary? launchOptions)
    {
        TextInputCrashGuard.Install();

        // With scenes, UIKit reports activation per window instead of calling
        // the app delegate, so the app-wide notifications are used instead.
        NSNotificationCenter.DefaultCenter.AddObserver(UIApplication.DidBecomeActiveNotification,
            _ => AppLifecycle.NotifyResumed());
        NSNotificationCenter.DefaultCenter.AddObserver(UIApplication.DidEnterBackgroundNotification,
            _ => AppLifecycle.NotifyBackground());

        return base.FinishedLaunching(application, launchOptions);
    }

    public override void BuildMenu(IUIMenuBuilder builder)
    {
        base.BuildMenu(builder);
        if (builder.System != UIMenuSystem.MainSystem)
            return;

        // Settings sits under About in the Valour menu, where Mac apps keep it.
        var settings = UIKeyCommand.Create((NSString)"Settings…", null, new Selector("valourOpenSettings:"),
            (NSString)",", UIKeyModifierFlags.Command, null);
        builder.InsertSiblingMenuAfter(
            UIMenu.Create(string.Empty, null, UIMenuIdentifier.None, UIMenuOptions.DisplayInline, [settings]),
            UIMenuIdentifier.About.GetConstant()!);

        var help = UICommand.Create("Valour Help", null, new Selector("valourOpenHelp:"), null);
        builder.ReplaceChildrenOfMenu(UIMenuIdentifier.Help.GetConstant()!, _ => [help]);

        // Text formatting comes from the app's own editor, not the system font panel.
        builder.RemoveMenu(UIMenuIdentifier.Format.GetConstant()!);

        // A second main window would run a second copy of the app. Extra
        // windows come only from popping out a tab.
        builder.RemoveMenu(UIMenuIdentifier.NewScene.GetConstant()!);
    }

    [Export("valourOpenSettings:")]
    private void OpenSettings(NSObject sender) => AppLifecycle.RequestSettings();

    [Export("valourOpenHelp:")]
    private void OpenHelp(NSObject sender) =>
        UIApplication.SharedApplication.OpenUrl(new NSUrl(HelpUrl), new UIApplicationOpenUrlOptions(), null);
}