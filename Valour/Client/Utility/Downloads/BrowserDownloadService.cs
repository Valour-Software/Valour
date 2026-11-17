using Microsoft.JSInterop;

namespace Valour.Client.Utility.Downloads;

/// <summary>
/// Hands downloads to the browser's own download manager, which shows
/// progress, asks where to save, and handles large files. This is the default.
/// </summary>
public class BrowserDownloadService : IDownloadService
{
    private const string ModulePath = "./_content/Valour.Client/js/downloads.js";

    private readonly IJSRuntime _js;
    private IJSObjectReference _module;

    public BrowserDownloadService(IJSRuntime js)
    {
        _js = js;
    }

    public bool UsesBrowserDownloads => true;

    public async Task<DownloadResult> DownloadAsync(
        DownloadRequest request,
        IProgress<DownloadProgress> progress = null,
        CancellationToken cancellationToken = default)
    {
        if (request is null || string.IsNullOrWhiteSpace(request.Url))
            return DownloadResult.Failed("This download link is not valid.");

        // A navigation cannot carry custom headers.
        if (request.Headers is { Count: > 0 })
            return DownloadResult.Failed("Browsers cannot download files that need request headers.");

        try
        {
            var module = await GetModuleAsync();
            await module.InvokeVoidAsync("downloadUrl", cancellationToken, request.Url,
                DownloadFileNames.Sanitize(request.FileName));
            return DownloadResult.Saved("Your browser is downloading the file.");
        }
        catch (JSException)
        {
            return DownloadResult.Failed("Your browser could not start the download.");
        }
        catch (JSDisconnectedException)
        {
            return DownloadResult.Failed("The page was closed before the download started.");
        }
    }

    public async Task<DownloadResult> SaveAsync(string fileName, string mimeType, byte[] data)
    {
        try
        {
            var module = await GetModuleAsync();
            await module.InvokeVoidAsync("downloadBytes", DownloadFileNames.Sanitize(fileName),
                string.IsNullOrWhiteSpace(mimeType) ? "application/octet-stream" : mimeType, data ?? []);
            return DownloadResult.Saved("Your browser is downloading the file.");
        }
        catch (JSException)
        {
            return DownloadResult.Failed("Your browser could not start the download.");
        }
        catch (JSDisconnectedException)
        {
            return DownloadResult.Failed("The page was closed before the download started.");
        }
    }

    private async Task<IJSObjectReference> GetModuleAsync() =>
        _module ??= await _js.ImportModuleAsync(ModulePath);
}
