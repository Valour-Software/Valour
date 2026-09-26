#if IOS || MACCATALYST
using System.Runtime.CompilerServices;
using Foundation;
using UIKit;
using WebKit;

namespace Valour.Client.Maui;

/// <summary>
/// Adds what the BlazorWebView's own WKUIDelegate leaves out on Apple
/// platforms. Calls grant microphone and camera access to the app's page
/// without a WebKit prompt each session, since macOS and iOS already ask once
/// for the app. Links that open a new window (target="_blank" or
/// window.open) go to the default browser; without a handler WebKit ignores
/// them. JavaScript alert, confirm, and prompt dialogs still go to the
/// BlazorWebView's delegate.
/// </summary>
public sealed class AppleWebViewUIDelegate : WKUIDelegate
{
    // WKWebView holds its UI delegate weakly, so the delegate lives as long
    // as its web view through this table.
    private static readonly ConditionalWeakTable<WKWebView, AppleWebViewUIDelegate> Attached = new();

    private readonly WKUIDelegate? _inner;

    private AppleWebViewUIDelegate(WKUIDelegate? inner)
    {
        _inner = inner;
    }

    public static void Attach(WKWebView webView)
    {
        if (webView.UIDelegate is AppleWebViewUIDelegate)
            return;

        var wrapper = new AppleWebViewUIDelegate(webView.UIDelegate as WKUIDelegate);
        Attached.AddOrUpdate(webView, wrapper);
        webView.UIDelegate = wrapper;
    }

    public override void RequestMediaCapturePermission(WKWebView webView, WKSecurityOrigin origin, WKFrameInfo frame,
        WKMediaCaptureType type, Action<WKPermissionDecision> decisionHandler)
    {
        decisionHandler(MainPage.IsInternalHost(origin.Host)
            ? WKPermissionDecision.Grant
            : WKPermissionDecision.Prompt);
    }

    public override WKWebView? CreateWebView(WKWebView webView, WKWebViewConfiguration configuration,
        WKNavigationAction navigationAction, WKWindowFeatures windowFeatures)
    {
        var url = navigationAction.Request.Url;
        if (url is not null && url.Scheme is "http" or "https" or "mailto" && !MainPage.IsInternalHost(url.Host))
            UIApplication.SharedApplication.OpenUrl(url, new UIApplicationOpenUrlOptions(), null);

        return null;
    }

    public override void RunJavaScriptAlertPanel(WKWebView webView, string message, WKFrameInfo frame, Action completionHandler)
    {
        if (_inner is null)
        {
            completionHandler();
            return;
        }

        _inner.RunJavaScriptAlertPanel(webView, message, frame, completionHandler);
    }

    public override void RunJavaScriptConfirmPanel(WKWebView webView, string message, WKFrameInfo frame, Action<bool> completionHandler)
    {
        if (_inner is null)
        {
            completionHandler(false);
            return;
        }

        _inner.RunJavaScriptConfirmPanel(webView, message, frame, completionHandler);
    }

    // The binding marks this obsolete, but it is the only managed override for
    // WebKit's text prompt callback.
    [Obsolete]
    public override void RunJavaScriptTextInputPanel(WKWebView webView, string prompt, string? defaultText, WKFrameInfo frame,
        Action<string> completionHandler)
    {
        if (_inner is null)
        {
            completionHandler(null!);
            return;
        }

        _inner.RunJavaScriptTextInputPanel(webView, prompt, defaultText, frame, completionHandler);
    }
}
#endif
