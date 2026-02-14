using Android.Content;
using Android.Webkit;

namespace Valour.Client.Maui;

/// <summary>
/// WebChromeClient that handles both microphone permission grants for WebRTC
/// and file chooser intents for Blazor InputFile / file upload.
/// </summary>
public class AudioPermissionChromeClient : WebChromeClient
{
    public const int FileChooserRequestCode = 1001;

    private static IValueCallback? _filePathCallback;

    public override void OnPermissionRequest(PermissionRequest? request)
    {
        if (request?.GetResources() is null)
        {
            base.OnPermissionRequest(request);
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
            var status = await Permissions.CheckStatusAsync<Permissions.Microphone>();
            if (status != PermissionStatus.Granted)
                status = await Permissions.RequestAsync<Permissions.Microphone>();

            if (status == PermissionStatus.Granted)
                request.Grant(request.GetResources());
            else
                request.Deny();
        }
        catch
        {
            request.Deny();
        }
    }
}
