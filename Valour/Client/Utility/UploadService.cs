using Microsoft.JSInterop;
using Valour.Shared.Utilities;

namespace Valour.Client.Utility;

/// <summary>
/// Reusable upload service for Blazor WASM that provides real wire-level
/// progress tracking and cancellation via XHR. Wrap all your uploads in this
/// and you never have to touch JS interop again.
/// </summary>
public class UploadService : IAsyncDisposable
{
    private readonly IJSRuntime _js;
    private IJSObjectReference? _module;
    private IJSObjectReference? _currentUpload;
    private DotNetObjectReference<UploadCallbacks>? _currentDotnetRef;
    private CancellationTokenSource? _cts;

    public UploadService(IJSRuntime jsRuntime)
    {
        _js = jsRuntime;
    }

    /// <summary>
    /// Upload a file with real progress tracking. Returns an UploadResult
    /// when the upload completes (success or failure).
    /// Cancel via the CancellationToken.
    /// </summary>
    public async Task<UploadResult> UploadAsync(
        string url,
        byte[] data,
        string mimeType,
        string fileName,
        string? authToken = null,
        Action<UploadProgressInfo>? onProgress = null,
        CancellationToken cancellationToken = default)
    {
        // Lazy-load the JS module on first use
        _module ??= await _js.InvokeAsync<IJSObjectReference>("import", "./ts/UploadService.js");

        // If there's a previous upload running, cancel it
        CancelCurrent();

        var tcs = new TaskCompletionSource<UploadResult>();
        _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

        var callbacks = new UploadCallbacks(tcs, onProgress);
        _currentDotnetRef = DotNetObjectReference.Create(callbacks);

        _currentUpload = await _module.InvokeAsync<IJSObjectReference>(
            "start",
            url,
            data,
            mimeType,
            fileName,
            _currentDotnetRef,
            authToken);

        // Wire up cancellation
        _cts.Token.Register(() =>
        {
            CancelCurrent();
            if (!tcs.Task.IsCompleted)
                tcs.TrySetResult(new UploadResult(false, null, "Upload cancelled"));
        });

        // Also handle the case where someone cancels the token before we even wire it
        if (cancellationToken.IsCancellationRequested)
        {
            CancelCurrent();
            return new UploadResult(false, null, "Upload cancelled");
        }

        return await tcs.Task;
    }

    /// <summary>
    /// Cancel whatever upload is currently in flight.
    /// </summary>
    public void CancelCurrent()
    {
        try
        {
            _currentUpload?.InvokeVoidAsync("abort").AsTask().Wait(TimeSpan.FromSeconds(2));
        }
        catch
        {
            // Best effort - if abort fails the upload will complete or error on its own
        }

        _currentUpload = null;
        CleanupDotnetRef();
    }

    private void CleanupDotnetRef()
    {
        _currentDotnetRef?.Dispose();
        _currentDotnetRef = null;
    }

    public async ValueTask DisposeAsync()
    {
        CancelCurrent();
        _cts?.Cancel();
        _cts?.Dispose();

        try
        {
            if (_module is not null)
                await _module.DisposeAsync();
        }
        catch (JSDisconnectedException) { }
        catch (JSException) { }

        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// Internal callback target for JSInvokable calls from the JS module.
    /// Bridges XHR events into the TaskCompletionSource and progress callback.
    /// </summary>
    private sealed class UploadCallbacks
    {
        private readonly TaskCompletionSource<UploadResult> _tcs;
        private readonly Action<UploadProgressInfo>? _onProgress;

        public UploadCallbacks(TaskCompletionSource<UploadResult> tcs, Action<UploadProgressInfo>? onProgress)
        {
            _tcs = tcs;
            _onProgress = onProgress;
        }

        [JSInvokable("NotifyUploadProgress")]
        public void NotifyUploadProgress(long loaded, long total)
        {
            _onProgress?.Invoke(new UploadProgressInfo(loaded, total));
        }

        [JSInvokable("NotifyUploadComplete")]
        public void NotifyUploadComplete(string response)
        {
            _tcs.TrySetResult(new UploadResult(true, response, null));
        }

        [JSInvokable("NotifyUploadMisdirect")]
        public void NotifyUploadMisdirect(string responseBody, int statusCode)
        {
            _tcs.TrySetResult(new UploadResult(false, null, $"Server redirect ({statusCode}). Please try again."));
        }

        [JSInvokable("NotifyUploadError")]
        public void NotifyUploadError(string error)
        {
            _tcs.TrySetResult(new UploadResult(false, null, error));
        }

        [JSInvokable("NotifyUploadCancelled")]
        public void NotifyUploadCancelled()
        {
            _tcs.TrySetResult(new UploadResult(false, null, "Upload cancelled"));
        }
    }
}

/// <summary>
/// The result of an upload operation. Clean and simple.
/// </summary>
public record UploadResult(bool Success, string? Response, string? Error)
{
    public bool IsMisdirect => !Success && Error?.Contains("redirect") == true;
}

/// <summary>
/// Real-time progress info from the XHR upload progress event.
/// BytesUploaded / TotalBytes are the real wire-level numbers.
/// </summary>
public record UploadProgressInfo(long BytesUploaded, long TotalBytes)
{
    public int Percent => TotalBytes > 0 ? (int)Math.Round((double)BytesUploaded / TotalBytes * 100) : 0;

    public string FormattedUploaded => FormatBytes(BytesUploaded);
    public string FormattedTotal => FormatBytes(TotalBytes);

    private static string FormatBytes(long bytes)
    {
        if (bytes < 1024) return $"{bytes} B";
        if (bytes < 1024 * 1024) return $"{bytes / 1024.0:F1} KB";
        return $"{bytes / (1024.0 * 1024.0):F1} MB";
    }
}
