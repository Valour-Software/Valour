using System.Runtime.Versioning;
using Android.Graphics;
using Android.Webkit;
using Sentry;
using AWebView = Android.Webkit.WebView;

namespace Valour.Client.Maui;

/// <summary>
/// Wraps the BlazorWebView's WebViewClient to survive Android killing the
/// WebView renderer process (screen-off while foregrounded, memory pressure
/// while backgrounded). Unhandled, a dead renderer leaves the app showing a
/// frozen last frame and the OS then kills the whole process; handled, we
/// swap in a fresh MainPage so the app reloads instead. Every other callback
/// is forwarded to the inner MAUI client, which serves the Blazor app.
/// </summary>
[SupportedOSPlatform("android26.0")]
public sealed class RendererCrashRecoveryWebViewClient : WebViewClient
{
    private readonly WebViewClient _inner;

    // Android delivers OnRenderProcessGone once per WebView, but guard anyway:
    // running recovery twice would tear down the freshly recreated page.
    private bool _handled;

    public RendererCrashRecoveryWebViewClient(WebViewClient inner)
    {
        _inner = inner;
    }

    public override bool OnRenderProcessGone(AWebView? view, RenderProcessGoneDetail? detail)
    {
        if (_handled)
            return true;
        _handled = true;

        var crashed = detail?.DidCrash() ?? false;
        Android.Util.Log.Warn("Valour",
            $"WebView renderer process gone (didCrash={crashed}). Recreating the app page.");
        SentrySdk.CaptureMessage(
            $"Android WebView renderer process gone (didCrash={crashed}); recovering by recreating MainPage.",
            SentryLevel.Warning);

        // Defer out of the WebView callback: swapping the page disconnects the
        // old BlazorWebView handler, which removes and destroys the dead
        // platform WebView, then a fresh one boots the Blazor app.
        MainThread.BeginInvokeOnMainThread(() =>
        {
            var window = Microsoft.Maui.Controls.Application.Current?.Windows.FirstOrDefault();
            if (window is not null)
            {
                window.Page = new MainPage();
            }
        });

        // True means we own recovery; returning false makes Android kill the app.
        return true;
    }

    // ----- Forwarders: the inner MAUI client serves the Blazor 0.0.0.0 origin -----

    public override bool ShouldOverrideUrlLoading(AWebView? view, IWebResourceRequest? request)
        => _inner.ShouldOverrideUrlLoading(view, request);

    public override WebResourceResponse? ShouldInterceptRequest(AWebView? view, IWebResourceRequest? request)
        => _inner.ShouldInterceptRequest(view, request);

    public override void OnPageStarted(AWebView? view, string? url, Bitmap? favicon)
        => _inner.OnPageStarted(view, url, favicon);

    public override void OnPageFinished(AWebView? view, string? url)
        => _inner.OnPageFinished(view, url);

    public override void OnLoadResource(AWebView? view, string? url)
        => _inner.OnLoadResource(view, url);

    public override void OnPageCommitVisible(AWebView? view, string? url)
        => _inner.OnPageCommitVisible(view, url);

    public override void OnReceivedError(AWebView? view, IWebResourceRequest? request, WebResourceError? error)
        => _inner.OnReceivedError(view, request, error);

    public override void OnReceivedHttpError(AWebView? view, IWebResourceRequest? request, WebResourceResponse? errorResponse)
        => _inner.OnReceivedHttpError(view, request, errorResponse);

    public override void DoUpdateVisitedHistory(AWebView? view, string? url, bool isReload)
        => _inner.DoUpdateVisitedHistory(view, url, isReload);
}
