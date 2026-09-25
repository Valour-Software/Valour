using Android.App;
using Android.Runtime;

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

    protected override MauiApp CreateMauiApp() => MauiProgram.CreateMauiApp();
}