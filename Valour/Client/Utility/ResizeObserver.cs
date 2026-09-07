using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;
using Valour.Shared.Utilities;

namespace Valour.Client.Utility;

public class ResizeObserver : IAsyncDisposable
{
    public HybridEvent<ElementDimensions> ResizeEvent;

    private IJSObjectReference _jsModule;
    private IJSObjectReference _service;
    private DotNetObjectReference<ResizeObserver> _dotnetRef;
    private bool _disposed;
    private bool _initializing;
    private Task _cleanupTask;

    public async Task Initialize(ElementReference element, IJSRuntime runtime, int debounce = 0)
    {
        if (_disposed || _initializing || _service is not null) return;
        _initializing = true;
        try
        {
            _jsModule = await runtime.InvokeAsync<IJSObjectReference>("import", "./_content/Valour.Client/ts/ResizeObserver.js");
            if (_disposed) return;
            _dotnetRef = DotNetObjectReference.Create(this);
            _service = await _jsModule.InvokeAsync<IJSObjectReference>("init", element, _dotnetRef, debounce);
            if (!_disposed && _service is not null)
                await _service.InvokeVoidAsync("observe");
        }
        catch (ObjectDisposedException) { }
        catch (JSException) { }
        finally
        {
            _initializing = false;
            if (_disposed)
                await CleanupAsync();
        }
    }

    public ValueTask DisposeAsync()
    {
        _disposed = true;
        ResizeEvent?.Dispose();
        return _initializing ? ValueTask.CompletedTask : new ValueTask(CleanupAsync());
    }

    private Task CleanupAsync() => _cleanupTask ??= ReleaseReferencesAsync();

    private async Task ReleaseReferencesAsync()
    {
        var service = _service;
        var module = _jsModule;
        _service = null;
        _jsModule = null;
        if (service is not null)
        {
            try { await service.InvokeVoidAsync("dispose"); }
            catch (JSException) { }
            catch (ObjectDisposedException) { }
            try { await service.DisposeAsync(); }
            catch (JSException) { }
            catch (ObjectDisposedException) { }
        }
        if (module is not null)
        {
            try { await module.DisposeAsync(); }
            catch (JSException) { }
            catch (ObjectDisposedException) { }
        }
        _dotnetRef?.Dispose();
        _dotnetRef = null;
    }

    [JSInvokable("NotifyResize")]
    public void NotifyResize(ElementDimensions dimensions)
    {
        if (!_disposed)
            ResizeEvent?.Invoke(dimensions);
    }
}
