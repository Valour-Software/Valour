using System.Reflection;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Photino.Blazor;
using Valour.Client.Device;
using Valour.Client.Notifications;
using Valour.Client.Photino.Notifications;
using Valour.Client.Photino.Storage;
using Valour.Client.Storage;

namespace Valour.Client.Photino;

/*  Valour (TM) - A free and secure chat client
 *  Copyright (C) 2025 Valour Software LLC
 *  This program is subject to the GNU Affero General Public license
 *  A copy of the license should be included - if not, see <http://www.gnu.org/licenses/>
 */

public static class Program
{
    private const string HostMessagePrefix = "valour-host:";

    [STAThread]
    public static void Main(string[] args)
    {
        var builder = PhotinoBlazorAppBuilder.CreateDefault(StaticWebAssets.CreateFileProvider(), args);

        builder.Services.AddSingleton<IAppStorage, FileAppStorage>();
        builder.Services.AddSingleton<Valour.Sdk.E2ee.IE2eeKeyStore, SecretServiceKeyStore>();
        builder.Services.AddSingleton<IPushNotificationService, DesktopNotificationService>();
        builder.Services.AddValourClientServices(ApiBaseAddress());

        // Provider sign-in uses the system browser, not the app's web view.
        builder.Services.AddScoped<IExternalAuthLauncher>(_ => new LoopbackExternalAuthLauncher(SystemBrowser.OpenAsync));

        builder.RootComponents.Add<ValourApp>("app");

        var app = builder.Build();

        app.MainWindow
            .SetLogVerbosity(0)
            .SetUserAgent(UserAgent())
            .SetTitle("Valour")
            .SetIconFile(Path.Combine(AppContext.BaseDirectory, "valour.png"))
            .SetSize(1280, 820)
            .SetMinSize(420, 560)
            .SetUseOsDefaultLocation(true)
            .SetDevToolsEnabled(IsDebug)
            .SetContextMenuEnabled(IsDebug)
            .SetGrantBrowserPermissions(true)
            .SetMediaStreamEnabled(true)
            .SetJavascriptClipboardAccessEnabled(true)
            .SetNotificationsEnabled(true)
            // Calls start playing remote audio without a click. On Linux this
            // flag is passed straight to media_playback_requires_user_gesture,
            // so it has the opposite meaning there.
            .SetMediaAutoplayEnabled(!OperatingSystem.IsLinux())
            .RegisterWebMessageReceivedHandler(OnWebMessage);

        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
            Console.Error.WriteLine($"Unhandled exception: {e.ExceptionObject}");

        app.Run();
    }

    private static void OnWebMessage(object? sender, string message)
    {
        if (!message.StartsWith(HostMessagePrefix, StringComparison.Ordinal))
            return;

        try
        {
            using var json = JsonDocument.Parse(message.AsMemory(HostMessagePrefix.Length));
            var root = json.RootElement;
            var type = root.GetProperty("type").GetString();
            if (type == "open" && Uri.TryCreate(root.GetProperty("url").GetString(), UriKind.Absolute, out var uri))
            {
                _ = SystemBrowser.OpenAsync(uri);
            }
            else if (type == "console" && IsDebug)
            {
                Console.Error.WriteLine($"[web {root.GetProperty("level").GetString()}] {root.GetProperty("text").GetString()}");
            }
        }
        catch (Exception ex) when (ex is JsonException or KeyNotFoundException or InvalidOperationException)
        {
            Console.Error.WriteLine($"Ignored a malformed host message: {ex.Message}");
        }
    }

    /// <summary>
    /// Photino reports "Photino WebView" by default. Libraries such as the
    /// voice client read the user agent to work around engine differences, so
    /// this reports the WebKit engine the web view is built on.
    /// </summary>
    private static string UserAgent()
    {
        var platform = OperatingSystem.IsMacOS() ? "Macintosh; Intel Mac OS X 10_15_7"
            : OperatingSystem.IsWindows() ? "Windows NT 10.0; Win64; x64"
            : $"X11; Linux {(System.Runtime.InteropServices.RuntimeInformation.OSArchitecture == System.Runtime.InteropServices.Architecture.Arm64 ? "aarch64" : "x86_64")}";
        var version = typeof(Program).Assembly.GetName().Version?.ToString(3) ?? "0";
        return $"Mozilla/5.0 ({platform}) AppleWebKit/605.1.15 (KHTML, like Gecko) Version/18.0 Safari/605.1.15 Valour/{version}";
    }

#if DEBUG
    private const bool IsDebug = true;
#else
    private const bool IsDebug = false;
#endif

    /// <summary>
    /// The official API, or the server named by the VALOUR_API_BASE
    /// environment variable in a Debug build.
    /// </summary>
    private static string ApiBaseAddress()
    {
#if DEBUG
        var local = Environment.GetEnvironmentVariable("VALOUR_API_BASE");
        if (!string.IsNullOrWhiteSpace(local))
            return local.TrimEnd('/');
#endif
        return "https://api.valour.gg";
    }
}
