using Android.Webkit;

namespace Valour.Client.Maui;

/// <summary>
/// WebChromeClient that requests the Android system microphone permission
/// and grants WebRTC media access when the BlazorWebView content calls getUserMedia.
/// </summary>
public class AudioPermissionChromeClient : WebChromeClient
{
    public override void OnPermissionRequest(PermissionRequest? request)
    {
        if (request?.GetResources() is null)
        {
            base.OnPermissionRequest(request);
            return;
        }

        _ = HandlePermissionRequestAsync(request);
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
