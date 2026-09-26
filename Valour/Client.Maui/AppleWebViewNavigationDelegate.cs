#if IOS || MACCATALYST
using System.Runtime.CompilerServices;
using Foundation;
using ObjCRuntime;
using UIKit;
using WebKit;

namespace Valour.Client.Maui;

/// <summary>
/// Adds what the BlazorWebView's own WKNavigationDelegate leaves out on Apple
/// platforms, and passes everything else to it. Links the page asks to
/// download (a download attribute, as on blob: exports) are saved to a
/// temporary file and offered through the system Save panel; without a
/// download handler WebKit ignores them. Iframes such as embeds load in place
/// instead of opening in the browser. When WebKit's content process ends
/// (memory pressure, a crash), the page reloads instead of staying blank.
/// </summary>
public sealed class AppleWebViewNavigationDelegate : WKNavigationDelegate
{
    // WKWebView holds its navigation delegate weakly, so the delegate lives as
    // long as its web view through this table.
    private static readonly ConditionalWeakTable<WKWebView, AppleWebViewNavigationDelegate> Attached = new();
    private static readonly DownloadSaver Saver = new();

    private readonly WKNavigationDelegate? _inner;

    private AppleWebViewNavigationDelegate(WKNavigationDelegate? inner)
    {
        _inner = inner;
    }

    public static void Attach(WKWebView webView)
    {
        if (webView.NavigationDelegate is AppleWebViewNavigationDelegate)
            return;

        var wrapper = new AppleWebViewNavigationDelegate(webView.NavigationDelegate as WKNavigationDelegate);
        Attached.AddOrUpdate(webView, wrapper);
        webView.NavigationDelegate = wrapper;
    }

    private bool InnerHandles(string selector) =>
        _inner is not null && _inner.RespondsToSelector(new Selector(selector));

    public override void DecidePolicy(WKWebView webView, WKNavigationAction navigationAction,
        Action<WKNavigationActionPolicy> decisionHandler)
    {
        if (navigationAction.ShouldPerformDownload)
        {
            decisionHandler(WKNavigationActionPolicy.Download);
            return;
        }

        // The BlazorWebView's delegate treats every navigation as a link the
        // person followed, so an embed's iframe loading (an X post, a video)
        // would open its page in the browser. Frames inside the page load in
        // place, as in a browser; only the app's own page goes through it.
        if (navigationAction.TargetFrame is { MainFrame: false })
        {
            decisionHandler(WKNavigationActionPolicy.Allow);
            return;
        }

        if (InnerHandles("webView:decidePolicyForNavigationAction:decisionHandler:"))
            _inner!.DecidePolicy(webView, navigationAction, decisionHandler);
        else
            decisionHandler(WKNavigationActionPolicy.Allow);
    }

    public override void NavigationActionDidBecomeDownload(WKWebView webView, WKNavigationAction navigationAction,
        WKDownload download) => download.Delegate = Saver;

    public override void ContentProcessDidTerminate(WKWebView webView) => webView.Reload();

    public override void DidStartProvisionalNavigation(WKWebView webView, WKNavigation navigation)
    {
        if (InnerHandles("webView:didStartProvisionalNavigation:"))
            _inner!.DidStartProvisionalNavigation(webView, navigation);
    }

    public override void DidReceiveServerRedirectForProvisionalNavigation(WKWebView webView, WKNavigation navigation)
    {
        if (InnerHandles("webView:didReceiveServerRedirectForProvisionalNavigation:"))
            _inner!.DidReceiveServerRedirectForProvisionalNavigation(webView, navigation);
    }

    public override void DidCommitNavigation(WKWebView webView, WKNavigation navigation)
    {
        if (InnerHandles("webView:didCommitNavigation:"))
            _inner!.DidCommitNavigation(webView, navigation);
    }

    public override void DidFinishNavigation(WKWebView webView, WKNavigation navigation)
    {
        if (InnerHandles("webView:didFinishNavigation:"))
            _inner!.DidFinishNavigation(webView, navigation);
    }

    public override void DidFailNavigation(WKWebView webView, WKNavigation navigation, NSError error)
    {
        if (InnerHandles("webView:didFailNavigation:withError:"))
            _inner!.DidFailNavigation(webView, navigation, error);
    }

    public override void DidFailProvisionalNavigation(WKWebView webView, WKNavigation navigation, NSError error)
    {
        if (InnerHandles("webView:didFailProvisionalNavigation:withError:"))
            _inner!.DidFailProvisionalNavigation(webView, navigation, error);
    }

    /// <summary>
    /// Writes each download to its own temporary folder, then lets the person
    /// choose where to keep it.
    /// </summary>
    private sealed class DownloadSaver : WKDownloadDelegate
    {
        private readonly Dictionary<WKDownload, string> _paths = new();

        public override void DecideDestination(WKDownload download, NSUrlResponse response, string suggestedFilename,
            Action<NSUrl> completionHandler)
        {
            var name = Path.GetFileName(suggestedFilename ?? string.Empty);
            if (string.IsNullOrWhiteSpace(name))
                name = "download";

            var folder = Path.Combine(Path.GetTempPath(), "valour-downloads", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(folder);
            var path = Path.Combine(folder, name);
            _paths[download] = path;
            completionHandler(NSUrl.FromFilename(path));
        }

        public override void DidFinish(WKDownload download)
        {
            if (!_paths.Remove(download, out var path))
                return;

            var folder = Path.GetDirectoryName(path)!;
            var picker = new UIDocumentPickerViewController([NSUrl.FromFilename(path)], true);
            picker.DidPickDocumentAtUrls += (_, _) => DeleteFolder(folder);
            picker.WasCancelled += (_, _) => DeleteFolder(folder);

            var presenter = Platform.GetCurrentUIViewController();
            if (presenter is null)
            {
                DeleteFolder(folder);
                return;
            }

            presenter.PresentViewController(picker, true, null);
        }

        public override void DidFail(WKDownload download, NSError error, NSData? resumeData)
        {
            if (_paths.Remove(download, out var path))
                DeleteFolder(Path.GetDirectoryName(path)!);
        }

        private static void DeleteFolder(string folder)
        {
            try
            {
                Directory.Delete(folder, true);
            }
            catch (IOException)
            {
                // The system clears temporary files eventually.
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }
}
#endif
