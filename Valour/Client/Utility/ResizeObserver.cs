using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;
using Valour.Shared.Utilities;

namespace Valour.Client.Utility;

public class ResizeObserver : IAsyncDisposable
{
    public HybridEvent<ElementDimensions> ResizeEvent;
    
    private ElementReference _element;
    private IJSRuntime _runtime;
    
    private IJSInProcessObjectReference _jsModule;
    private IJSInProcessObjectReference _service;
    
    private DotNetObjectReference<ResizeObserver> _dotnetRef;
    
    public async Task Initialize(ElementReference el, IJSRuntime runtime, int debounce = 0)
    {
        _element = el;
        _runtime = runtime;
        
        _dotnetRef = DotNetObjectReference.Create(this);
        
        _jsModule = await _runtime.InvokeAsync<IJSInProcessObjectReference>("import", "./_content/Valour.Client/ts/ResizeObserver.js");
        _service = await _jsModule.InvokeAsync<IJSInProcessObjectReference>("init", _element, _dotnetRef, debounce);
        await _service.InvokeVoidAsync("observe");
    }
    
    public async ValueTask DisposeAsync()
    {
        await _service.InvokeVoidAsync("dispose");
        _dotnetRef.Dispose();
        await _service.DisposeAsync();
        await _jsModule.DisposeAsync();
        
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