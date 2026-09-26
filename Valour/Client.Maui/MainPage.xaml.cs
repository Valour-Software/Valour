using System.Diagnostics;
#if WINDOWS
using Microsoft.UI.Xaml.Controls;
using Microsoft.Web.WebView2.Core;
#endif

namespace Valour.Client.Maui;

public partial class MainPage : ContentPage
{
    public MainPage(string? startPath = null)
    {
        InitializeComponent();
        if (!string.IsNullOrWhiteSpace(startPath))
        {
            blazorWebView.StartPath = startPath;
        }

        blazorWebView.BlazorWebViewInitialized += OnBlazorWebViewInitialized;
        blazorWebView.UrlLoading += OnUrlLoading;
    }

    private void OnBlazorWebViewInitialized(object? sender, Microsoft.AspNetCore.Components.WebView.BlazorWebViewInitializedEventArgs e)
    {
        Debug.WriteLine("BlazorWebView initialized");

#if WINDOWS
        ConfigureWindowsWebViewPermissions(e);
#endif
    }

    private void OnUrlLoading(object? sender, Microsoft.AspNetCore.Components.WebView.UrlLoadingEventArgs e)
    {
        // External URLs must be opened in the system browser.
        // Without this, all link clicks are silently swallowed by the WebView.
        if (!IsInternalHost(e.Url.Host))
        {
            e.UrlLoadingStrategy = Microsoft.AspNetCore.Components.WebView.UrlLoadingStrategy.OpenExternally;
        }
    }

    internal static bool IsInternalHost(string? host)
    {
        return string.Equals(host, "0.0.0.0", StringComparison.OrdinalIgnoreCase)
               || string.Equals(host, "0.0.0.1", StringComparison.OrdinalIgnoreCase)
               || string.Equals(host, "localhost", StringComparison.OrdinalIgnoreCase)
               || string.Equals(host, "127.0.0.1", StringComparison.OrdinalIgnoreCase);
    }

#if WINDOWS
    private void ConfigureWindowsWebViewPermissions(Microsoft.AspNetCore.Components.WebView.BlazorWebViewInitializedEventArgs e)
    {
        if (e.WebView is not WebView2 webView)
        {
            return;
        }

        if (webView.CoreWebView2 is not null)
        {
            AttachPermissionHandler(webView.CoreWebView2);
            return;
        }

        webView.CoreWebView2Initialized += (_, args) =>
        {
            if (args.Exception is not null)
            {
                Debug.WriteLine($"WebView2 initialization failed: {args.Exception}");
                return;
            }

            if (webView.CoreWebView2 is not null)
            {
                AttachPermissionHandler(webView.CoreWebView2);
            }
        };
    }

    private static void AttachPermissionHandler(CoreWebView2 coreWebView2)
    {
        coreWebView2.PermissionRequested -= OnPermissionRequested;
        coreWebView2.PermissionRequested += OnPermissionRequested;
    }

    private static void OnPermissionRequested(object? sender, CoreWebView2PermissionRequestedEventArgs e)
    {
        if (!IsTrustedPermissionRequestOrigin(e.Uri))
        {
            return;
        }

        if (!IsMediaPermissionKind(e.PermissionKind))
        {
            return;
        }

        e.State = CoreWebView2PermissionState.Allow;
    }

    private static bool IsTrustedPermissionRequestOrigin(string? uri)
    {
        if (!Uri.TryCreate(uri, UriKind.Absolute, out var parsed))
        {
            return false;
        }

        return IsInternalHost(parsed.Host);
    }

    private static bool IsMediaPermissionKind(CoreWebView2PermissionKind permissionKind)
    {
        if (permissionKind == CoreWebView2PermissionKind.Microphone ||
            permissionKind == CoreWebView2PermissionKind.Camera)
        {
            return true;
        }

        // Screen capture permission names vary by WebView2 runtime versions.
        var kindName = permissionKind.ToString();
        return string.Equals(kindName, "DisplayCapture", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(kindName, "ScreenCapture", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(kindName, "DesktopCapture", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(kindName, "WindowManagement", StringComparison.OrdinalIgnoreCase);
    }
#endif
}
