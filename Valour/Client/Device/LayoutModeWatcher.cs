using Microsoft.JSInterop;

namespace Valour.Client.Device;

/// <summary>
/// Chooses the phone or wide layout from the window size in the iPhone and iPad
/// app, and switches it when the window changes size, such as when an iPhone Duo
/// opens or closes. The size rule lives in valourLayout in main.js.
/// </summary>
public sealed class LayoutModeWatcher
{
    private static DotNetObjectReference<LayoutModeWatcher> _reference;

    /// <summary>
    /// Returns whether the window currently fits the phone layout, and starts
    /// reporting changes to <see cref="DeviceInfo.SetMobileLayout"/>.
    /// </summary>
    public static async Task<bool> StartAsync(IJSRuntime js)
    {
        var wide = await js.InvokeAsync<bool>("valourLayout.isWide");

        if (_reference is null)
        {
            _reference = DotNetObjectReference.Create(new LayoutModeWatcher());
            await js.InvokeVoidAsync("valourLayout.watch", _reference);
        }

        return !wide;
    }

    [JSInvokable]
    public void OnWideLayoutChanged(bool wide) => DeviceInfo.SetMobileLayout(!wide);
}
