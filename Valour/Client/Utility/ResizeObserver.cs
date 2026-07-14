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

        try
        {
            var module = await _runtime.InvokeAsync<IJSObjectReference>("import", "./_content/Valour.Client/ts/ResizeObserver.js");

            // The owning component can be disposed while we await above. If so,
            // _dotnetRef is already disposed and passing it to JS would throw
            // ObjectDisposedException, so bail out and clean up what we created.
            if (_disposed)
            {
                await module.DisposeAsync();
                return;
            }

            _jsModule = module;

            var service = await _jsModule.InvokeAsync<IJSObjectReference>("init", _element, _dotnetRef, debounce);

            if (_disposed)
            {
                await service.DisposeAsync();
                return;
            }

            _service = service;

            await _service.InvokeVoidAsync("observe");
        }
        catch (ObjectDisposedException) { }
        catch (JSDisconnectedException) { }
        catch (JSException) { }
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