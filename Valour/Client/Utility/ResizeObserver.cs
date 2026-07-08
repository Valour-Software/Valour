using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;
using Valour.Shared.Utilities;

namespace Valour.Client.Utility;

public class ResizeObserver : IAsyncDisposable
{
    public HybridEvent<ElementDimensions> ResizeEvent;
    
    private ElementReference _element;
    private IJSRuntime _runtime;
    
    private IJSObjectReference _jsModule;
    private IJSObjectReference _service;
    
    private DotNetObjectReference<ResizeObserver> _dotnetRef;
    private bool _disposed;

    public async Task Initialize(ElementReference el, IJSRuntime runtime, int debounce = 0)
    {
        _element = el;
        _runtime = runtime;

        _dotnetRef = DotNetObjectReference.Create(this);

        _jsModule = await _runtime.InvokeAsync<IJSObjectReference>("import", "./_content/Valour.Client/ts/ResizeObserver.js");

        if (_disposed)
            return;

        _service = await _jsModule.InvokeAsync<IJSObjectReference>("init", _element, _dotnetRef, debounce);

        if (_disposed)
            return;

        await _service.InvokeVoidAsync("observe");
    }

    public async ValueTask DisposeAsync()
    {
        _disposed = true;

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

        if (ResizeEvent is not null)
            ResizeEvent.Dispose();

        GC.SuppressFinalize(this);
    }
    
    [JSInvokable("NotifyResize")]
    public void NotifyResize(ElementDimensions dimensions)
    {
        if (ResizeEvent is not null)
            ResizeEvent.Invoke(dimensions);
    }
}