using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;

namespace Valour.Client.Utility;

/// <summary>
/// Reusable swipe-action helper for touch devices. Attach to a container
/// element and any descendant matching the item selector can be swiped
/// left to reveal an action icon; releasing past the threshold fires
/// <see cref="SlideAction"/> with the swiped element's id.
/// Mouse input is unaffected (touch events never fire for it).
/// </summary>
public class TouchSlide : IAsyncDisposable
{
    /// <summary>
    /// Fired with the element id of the item that was swiped past the threshold.
    /// </summary>
    public event Func<string, Task> SlideAction;

    private IJSObjectReference _jsModule;
    private IJSObjectReference _service;
    private DotNetObjectReference<TouchSlide> _dotnetRef;

    public async Task Initialize(
        ElementReference container,
        string itemSelector,
        IJSRuntime runtime,
        int threshold = 56,
        int maxSlide = 96,
        string iconHtml = "<i class=\"bi bi-reply-fill\"></i>")
    {
        _dotnetRef = DotNetObjectReference.Create(this);

        _jsModule = await runtime.InvokeAsync<IJSObjectReference>("import", "./_content/Valour.Client/ts/TouchSlide.js");
        _service = await _jsModule.InvokeAsync<IJSObjectReference>("init", container, itemSelector, _dotnetRef, threshold, maxSlide, iconHtml);
    }

    [JSInvokable("OnSlideAction")]
    public async Task OnSlideAction(string elementId)
    {
        if (SlideAction is not null)
            await SlideAction.Invoke(elementId);
    }

    public async ValueTask DisposeAsync()
    {
        try
        {
            if (_service is not null)
            {
                await _service.InvokeVoidAsync("dispose");
                await _service.DisposeAsync();
            }

            if (_jsModule is not null)
                await _jsModule.DisposeAsync();
        }
        catch (JSDisconnectedException) { }
        catch (JSException) { }

        _dotnetRef?.Dispose();

        GC.SuppressFinalize(this);
    }
}
