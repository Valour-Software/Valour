namespace Valour.Client.Maui;

public partial class App : Application
{
    public App()
    {
        InitializeComponent();
#if MACCATALYST
        // The app draws its own dark interface, so the title bar, scroll bars,
        // and system pickers match it instead of following the system setting.
        UserAppTheme = AppTheme.Dark;
#endif
    }

    protected override Window CreateWindow(IActivationState? activationState)
    {
        var window = new Window(new MainPage()) { Title = "Valour" };
#if MACCATALYST
        // Below this size the desktop layout's sidebars crowd out the chat.
        window.MinimumWidth = 720;
        window.MinimumHeight = 480;
#endif
        return window;
    }
}
