using Android.Webkit;

namespace Valour.Client.Maui;

/// <summary>
/// WebChromeClient that grants WebRTC media permissions (microphone, camera)
/// when requested by the BlazorWebView content.
/// </summary>
public class AudioPermissionChromeClient : WebChromeClient
{
    public override void OnPermissionRequest(PermissionRequest? request)
    {
        // Grant all requested resources — this is a first-party WebView,
        // not a general-purpose browser, so granting is safe.
        request?.Grant(request.GetResources());
    }
}
