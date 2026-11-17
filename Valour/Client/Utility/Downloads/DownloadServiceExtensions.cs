using Valour.Client.Toast;

namespace Valour.Client.Utility.Downloads;

public static class DownloadServiceExtensions
{
    /// <summary>
    /// Downloads a file and tells the person how it went. A browser shows its own
    /// download progress, so only failures are announced there. Native hosts also
    /// confirm where the file was saved.
    /// </summary>
    public static async Task<DownloadResult> DownloadWithToastAsync(
        this IDownloadService downloads,
        DownloadRequest request,
        IProgress<DownloadProgress> progress = null,
        CancellationToken cancellationToken = default)
    {
        var result = await downloads.DownloadAsync(request, progress, cancellationToken);
        ShowResult(downloads, request.FileName, result);
        return result;
    }

    /// <summary>
    /// Saves in-memory content and tells the person how it went.
    /// </summary>
    public static async Task<DownloadResult> SaveWithToastAsync(
        this IDownloadService downloads, string fileName, string mimeType, byte[] data)
    {
        var result = await downloads.SaveAsync(fileName, mimeType, data);
        ShowResult(downloads, fileName, result);
        return result;
    }

    private static void ShowResult(IDownloadService downloads, string fileName, DownloadResult result)
    {
        if (ToastContainer.Instance is null)
            return;

        if (!result.Success)
        {
            ToastContainer.Instance.AddToast(new ToastData("Download failed", result.Message, ToastProgressState.Failure));
        }
        else if (!downloads.UsesBrowserDownloads)
        {
            ToastContainer.Instance.AddToast(new ToastData("Download complete", result.Message, ToastProgressState.Success));
        }
    }
}
