using Microsoft.Maui.ApplicationModel.DataTransfer;
using Valour.Client.Utility;

namespace Valour.Client.Maui;

/// <summary>
/// Presents the native OS share sheet (Android share system, iOS share sheet, etc.)
/// </summary>
public class MauiShareService : IShareService
{
    public async Task<bool> ShareAsync(string title, string text, string url)
    {
        try
        {
            await MainThread.InvokeOnMainThreadAsync(() =>
                Share.Default.RequestAsync(new ShareTextRequest
                {
                    Title = title,
                    Text = text,
                    Uri = url
                }));

            return true;
        }
        catch
        {
            return false;
        }
    }
}
