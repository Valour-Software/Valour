using Android.Content;
using Android.OS;
using Android.Webkit;

namespace Valour.Client.Maui;

/// <summary>
/// WebChromeClient that handles both microphone permission grants for WebRTC
/// and file chooser intents for Blazor InputFile / file upload.
/// </summary>
public class AudioPermissionChromeClient : WebChromeClient
{
    /// <summary>
    /// Handles target="_blank" link clicks by opening them in the system browser.
    /// Without this, all target="_blank" links are silently consumed by the WebView.
    /// </summary>
    public override bool OnCreateWindow(Android.Webkit.WebView? view, bool isDialog, bool isUserGesture, Message? resultMsg)
    {
        if (view is null)
            return false;

        var hitTestResult = view.GetHitTestResult();
        var url = hitTestResult?.Extra;

        // Only hand ordinary web and mail links to the system. Other schemes
        // (intent:, file:, content:, custom app schemes) could launch arbitrary
        // activities on behalf of page content.
        var uri = string.IsNullOrEmpty(url) ? null : Android.Net.Uri.Parse(url);
        if (uri is not null && IsExternalSchemeAllowed(uri.Scheme))
        {
            var intent = new Intent(Intent.ActionView, uri);
            intent.AddFlags(ActivityFlags.NewTask);
            view.Context?.StartActivity(intent);
        }

        return false;
    }

    private static bool IsExternalSchemeAllowed(string? scheme)
    {
        return string.Equals(scheme, "http", StringComparison.OrdinalIgnoreCase)
               || string.Equals(scheme, "https", StringComparison.OrdinalIgnoreCase)
               || string.Equals(scheme, "mailto", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// True when the permission request comes from the Blazor app itself.
    /// BlazorWebView serves the app from https://0.0.0.1 (or https://0.0.0.0
    /// when the legacy address switch is set); embedded third-party frames
    /// have their own origins and must never inherit microphone access.
    /// </summary>
    private static bool IsAppOrigin(Android.Net.Uri? origin)
    {
        if (origin is null || !string.Equals(origin.Scheme, "https", StringComparison.OrdinalIgnoreCase))
            return false;

        return string.Equals(origin.Host, "0.0.0.1", StringComparison.OrdinalIgnoreCase)
               || string.Equals(origin.Host, "0.0.0.0", StringComparison.OrdinalIgnoreCase);
    }

    public const int FileChooserRequestCode = 1001;

    private static IValueCallback? _filePathCallback;

    public override void OnPermissionRequest(PermissionRequest? request)
    {
        if (request?.GetResources() is null)
        {
            base.OnPermissionRequest(request);
            return;
        }

        if (!IsAppOrigin(request.Origin))
        {
            request.Deny();
            return;
        }

        _ = HandlePermissionRequestAsync(request);
    }

    public override bool OnShowFileChooser(
        Android.Webkit.WebView? webView,
        IValueCallback? filePathCallback,
        FileChooserParams? fileChooserParams)
    {
        // Cancel any previous pending callback
        _filePathCallback?.OnReceiveValue(null);
        _filePathCallback = filePathCallback;

        try
        {
            var intent = fileChooserParams?.CreateIntent();
            if (intent is null)
            {
                intent = new Intent(Intent.ActionGetContent);
                intent.SetType("*/*");
                intent.AddCategory(Intent.CategoryOpenable);
            }
            
            if (fileChooserParams?.Mode == ChromeFileChooserMode.OpenMultiple)
                intent.PutExtra(Intent.ExtraAllowMultiple, true);

            var activity = Platform.CurrentActivity;
            if (activity is null)
            {
                _filePathCallback?.OnReceiveValue(null);
                _filePathCallback = null;
                return false;
            }

            activity.StartActivityForResult(
                Intent.CreateChooser(intent, "Choose File"),
                FileChooserRequestCode);

            return true;
        }
        catch
        {
            _filePathCallback?.OnReceiveValue(null);
            _filePathCallback = null;
            return false;
        }
    }

    /// <summary>
    /// Must be called from MainActivity.OnActivityResult to deliver the file
    /// picker result back to the WebView.
    /// </summary>
    public static void HandleFileChooserResult(Android.App.Result resultCode, Intent? data)
    {
        if (_filePathCallback is null)
            return;

        Android.Net.Uri[]? results = null;

        if (resultCode == Android.App.Result.Ok && data is not null)
        {
            // Single file
            if (data.Data is not null)
            {
                results = new[] { data.Data };
            }
            // Multiple files
            else if (data.ClipData is not null)
            {
                results = new Android.Net.Uri[data.ClipData.ItemCount];
                for (var i = 0; i < data.ClipData.ItemCount; i++)
                    results[i] = data.ClipData.GetItemAt(i)!.Uri!;
            }
        }

        _filePathCallback.OnReceiveValue(results);
        _filePathCallback = null;
    }

    private static async Task HandlePermissionRequestAsync(PermissionRequest request)
    {
        try
        {
            // Grant only capture resources the app uses, and only once the
            // matching Android runtime permission is held. Other resources
            // (protected media ids, MIDI sysex) are never granted.
            var requested = request.GetResources() ?? Array.Empty<string>();
            var granted = new List<string>();

            if (requested.Contains(PermissionRequest.ResourceAudioCapture) &&
                await EnsurePermissionAsync<Permissions.Microphone>())
            {
                granted.Add(PermissionRequest.ResourceAudioCapture);
            }

            if (requested.Contains(PermissionRequest.ResourceVideoCapture) &&
                await EnsurePermissionAsync<Permissions.Camera>())
            {
                granted.Add(PermissionRequest.ResourceVideoCapture);
            }

            if (granted.Count > 0)
                request.Grant(granted.ToArray());
            else
                request.Deny();
        }
        catch
        {
            request.Deny();
        }
    }

    private static async Task<bool> EnsurePermissionAsync<TPermission>()
        where TPermission : Permissions.BasePermission, new()
    {
        try
        {
            var status = await Permissions.CheckStatusAsync<TPermission>();
            if (status != PermissionStatus.Granted)
                status = await Permissions.RequestAsync<TPermission>();

            return status == PermissionStatus.Granted;
        }
        catch (PermissionException)
        {
            // Thrown when the permission is not declared in the manifest.
            return false;
        }
    }
}
