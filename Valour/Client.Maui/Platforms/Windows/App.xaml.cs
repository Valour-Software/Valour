using Microsoft.UI.Xaml;
using WinUiControls = Microsoft.UI.Xaml.Controls;
using H.NotifyIcon;
using Sentry;
using System.Diagnostics;
using System.Collections.Concurrent;
using System.Threading;

namespace Valour.Client.Maui.WinUI;

public partial class App : MauiWinUIApplication
{
    private const string SingleInstanceMutexName = @"Local\Valour.Client.Maui.Windows.Singleton";
    private const string ShowWindowEventName = @"Local\Valour.Client.Maui.Windows.ShowWindow";

    private static Mutex? _singleInstanceMutex;
    private static EventWaitHandle? _showWindowEvent;
    private static Thread? _showWindowListenerThread;
    private static readonly ConcurrentDictionary<string, DateTime> _recentUnobservedExceptionFingerprints = new();

    private TaskbarIcon? _trayIcon;
    private Microsoft.UI.Xaml.Window? _mauiWindow;
    private Microsoft.UI.Windowing.AppWindow? _appWindow;
    private bool _isExiting;

    public App()
    {
        this.InitializeComponent();
        RegisterExceptionHandlers();
    }

    protected override MauiApp CreateMauiApp() => MauiProgram.CreateMauiApp();

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        if (!TryBecomePrimaryInstance())
        {
            SignalPrimaryInstanceToShow();
            ForceExitProcess();
            return;
        }

        base.OnLaunched(args);

        var window = Microsoft.Maui.MauiWinUIApplication.Current.Application.Windows[0];
        var mauiWindow = window.Handler?.PlatformView as Microsoft.UI.Xaml.Window;
        if (mauiWindow is null) return;
        _mauiWindow = mauiWindow;

        var windowHandle = WinRT.Interop.WindowNative.GetWindowHandle(mauiWindow);
        var windowId = Microsoft.UI.Win32Interop.GetWindowIdFromWindow(windowHandle);
        _appWindow = Microsoft.UI.Windowing.AppWindow.GetFromWindowId(windowId);

        try
        {
            var iconPath = GetIconPath();
            if (_appWindow is not null && iconPath is not null)
                _appWindow.SetIcon(iconPath);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Could not set the Valour window icon: {ex.Message}");
        }

        // Intercept the close button to minimize to tray instead
        if (_appWindow is not null)
        {
            _appWindow.Closing += (_, e) =>
            {
                if (_isExiting) return; // Allow close when actually exiting
                e.Cancel = true;
                mauiWindow.DispatcherQueue.TryEnqueue(() =>
                {
                    _mauiWindow?.Hide();
                });
            };
        }

        SetupTrayIcon();
        StartShowWindowSignalListener();
    }

    private void SetupTrayIcon()
    {
        _trayIcon = new TaskbarIcon
        {
            ToolTipText = "Valour",
            DoubleClickCommand = new RelayCommand(ShowWindow),
            Visibility =  Microsoft.UI.Xaml.Visibility.Visible,
        };

        // Try to load the app icon
        try
        {
            var iconPath = GetIconPath();
            if (iconPath is not null)
            {
                _trayIcon.Icon = new System.Drawing.Icon(iconPath);
            }
        }
        catch
        {
            // Fall back to default if icon file not found
        }

        // If the custom icon is unavailable, fall back to the executable icon.
        if (_trayIcon.Icon is null)
        {
            try
            {
                var processPath = Environment.ProcessPath;
                if (!string.IsNullOrWhiteSpace(processPath))
                {
                    _trayIcon.Icon = System.Drawing.Icon.ExtractAssociatedIcon(processPath);
                }
            }
            catch
            {
                // Best-effort fallback icon
            }
        }

        // Right-click context menu
        var menu = new WinUiControls.MenuFlyout();
        var showItem = new WinUiControls.MenuFlyoutItem { Text = "Show Valour" };
        showItem.Command = new RelayCommand(ShowWindow);
        showItem.Click += (_, _) => ShowWindow();
        menu.Items.Add(showItem);

        menu.Items.Add(new WinUiControls.MenuFlyoutSeparator());

        var exitItem = new WinUiControls.MenuFlyoutItem { Text = "Exit" };
        exitItem.Command = new RelayCommand(ExitApplication);
        exitItem.Click += (_, _) => ExitApplication();
        menu.Items.Add(exitItem);

        _trayIcon.ContextFlyout = menu;
        _trayIcon.ForceCreate();
    }

    private static string? GetIconPath()
    {
        var path = System.IO.Path.Combine(AppContext.BaseDirectory, "Platforms", "Windows", "trayicon.ico");
        return System.IO.File.Exists(path) ? path : null;
    }

    private void ShowWindow()
    {
        _mauiWindow?.Show();
        _appWindow?.Show();
        if (_appWindow?.Presenter is Microsoft.UI.Windowing.OverlappedPresenter presenter)
        {
            presenter.IsMinimizable = true;
            presenter.Restore();
        }
    }

    private void ExitApplication()
    {
        if (_isExiting)
        {
            return;
        }

        _isExiting = true;

        void Shutdown()
        {
            try
            {
                _trayIcon?.Dispose();
                _trayIcon = null;
            }
            catch
            {
                // Best-effort cleanup
            }

            try
            {
                Microsoft.Toolkit.Uwp.Notifications.ToastNotificationManagerCompat.Uninstall();
            }
            catch
            {
                // Best-effort cleanup
            }

            try
            {
                _appWindow?.Destroy();
            }
            catch
            {
                // Best-effort cleanup
            }

            try
            {
                _showWindowEvent?.Set();
            }
            catch
            {
                // Best-effort cleanup
            }

            try
            {
                _showWindowEvent?.Dispose();
                _showWindowEvent = null;
            }
            catch
            {
                // Best-effort cleanup
            }

            try
            {
                _singleInstanceMutex?.ReleaseMutex();
            }
            catch
            {
                // Ignore if not owned or already released
            }

            try
            {
                _singleInstanceMutex?.Dispose();
                _singleInstanceMutex = null;
            }
            catch
            {
                // Best-effort cleanup
            }

            ForceExitProcess();
        }

        var dispatcher = _mauiWindow?.DispatcherQueue;
        if (dispatcher is not null && !dispatcher.HasThreadAccess)
        {
            if (dispatcher.TryEnqueue(Shutdown))
            {
                return;
            }
        }

        Shutdown();
    }

    internal void ExitForUpdate() => ExitApplication();

    private static bool TryBecomePrimaryInstance()
    {
        if (_singleInstanceMutex is not null)
        {
            return true;
        }

        try
        {
            _singleInstanceMutex = new Mutex(initiallyOwned: true, name: SingleInstanceMutexName, createdNew: out var createdNew);
            if (!createdNew)
            {
                _singleInstanceMutex.Dispose();
                _singleInstanceMutex = null;
                return false;
            }

            _showWindowEvent = new EventWaitHandle(initialState: false, mode: EventResetMode.AutoReset, name: ShowWindowEventName);
            return true;
        }
        catch (Exception ex)
        {
            Debug.WriteLine(ex);
            return true;
        }
    }

    private static void SignalPrimaryInstanceToShow()
    {
        try
        {
            using var showEvent = EventWaitHandle.OpenExisting(ShowWindowEventName);
            showEvent.Set();
        }
        catch
        {
            // No existing signal handle available.
        }
    }

    private void StartShowWindowSignalListener()
    {
        var showWindowEvent = _showWindowEvent;
        if (showWindowEvent is null || _showWindowListenerThread is not null)
        {
            return;
        }

        _showWindowListenerThread = new Thread(() =>
        {
            while (!_isExiting)
            {
                try
                {
                    showWindowEvent.WaitOne();
                    if (_isExiting)
                    {
                        return;
                    }

                    _mauiWindow?.DispatcherQueue.TryEnqueue(ShowWindow);
                }
                catch
                {
                    return;
                }
            }
        })
        {
            IsBackground = true,
            Name = "ValourShowWindowSignalListener"
        };

        _showWindowListenerThread.Start();
    }

    private static void ForceExitProcess()
    {
        try
        {
            Microsoft.UI.Xaml.Application.Current.Exit();
        }
        catch (Exception ex)
        {
            Debug.WriteLine(ex);
        }

        try
        {
            Environment.Exit(0);
        }
        catch (Exception ex)
        {
            Debug.WriteLine(ex);
        }

        try
        {
            Process.GetCurrentProcess().Kill(true);
        }
        catch (Exception ex)
        {
            Debug.WriteLine(ex);
        }
    }

    private class RelayCommand : System.Windows.Input.ICommand
    {
        private readonly Action _execute;
        public RelayCommand(Action execute) => _execute = execute;
        public event EventHandler? CanExecuteChanged
        {
            add { }
            remove { }
        }
        public bool CanExecute(object? parameter) => true;
        public void Execute(object? parameter) => _execute();
    }

    private void RegisterExceptionHandlers()
    {
        UnhandledException += (_, e) =>
        {
            try
            {
                if (e.Exception is not null)
                {
                    SentrySdk.CaptureException(e.Exception);
                }
            }
            catch
            {
                // Best-effort reporting only.
            }
        };

        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
        {
            try
            {
                if (e.ExceptionObject is Exception ex)
                {
                    SentrySdk.CaptureException(ex);
                }
            }
            catch
            {
                // Best-effort reporting only.
            }
        };

        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            try
            {
                // Mark observed first so the finalizer thread doesn't escalate this again.
                e.SetObserved();

                var aggregate = e.Exception?.Flatten();
                var innerExceptions = aggregate?.InnerExceptions;
                if (innerExceptions is null || innerExceptions.Count == 0)
                {
                    return;
                }

                foreach (var inner in innerExceptions)
                {
                    if (inner is null)
                    {
                        continue;
                    }

                    if (!ShouldCaptureUnobservedException(inner))
                    {
                        continue;
                    }

                    SentrySdk.CaptureException(inner, scope =>
                    {
                        scope.SetTag("exception_source", "taskscheduler.unobserved");
                        scope.SetExtra("aggregate_exception_type", aggregate?.GetType().FullName ?? "unknown");
                        scope.SetExtra("aggregate_exception_message", aggregate?.Message ?? string.Empty);
                    });
                }
            }
            catch
            {
                // Best-effort reporting only.
            }
        };
    }

    private static bool ShouldCaptureUnobservedException(Exception exception)
    {
        // Ignore expected cancellation noise.
        if (exception is OperationCanceledException)
        {
            return false;
        }

        var fingerprint = $"{exception.GetType().FullName}|{exception.Message}|{exception.StackTrace}";
        var now = DateTime.UtcNow;

        if (_recentUnobservedExceptionFingerprints.TryGetValue(fingerprint, out var lastSeen) &&
            (now - lastSeen).TotalSeconds < 30)
        {
            return false;
        }

        _recentUnobservedExceptionFingerprints[fingerprint] = now;

        // Keep the in-memory dedupe map bounded.
        if (_recentUnobservedExceptionFingerprints.Count > 512)
        {
            foreach (var item in _recentUnobservedExceptionFingerprints)
            {
                if ((now - item.Value).TotalMinutes > 5)
                {
                    _recentUnobservedExceptionFingerprints.TryRemove(item.Key, out _);
                }
            }
        }

        return true;
    }
}
