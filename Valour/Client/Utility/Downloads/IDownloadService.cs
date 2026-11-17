namespace Valour.Client.Utility.Downloads;

/// <summary>
/// Saves files to the person's device. Every part of the app that hands a file
/// to the person goes through this service, because each host saves files
/// differently: browsers use their own download manager, desktop apps write
/// to the Downloads folder, Android uses the shared Downloads collection, and
/// iOS asks where to keep the file.
/// </summary>
public interface IDownloadService
{
    /// <summary>
    /// True when the browser performs the download. The call then returns as
    /// soon as the browser has been asked to start, and neither progress nor
    /// the final outcome is visible to the app. Native hosts finish the
    /// transfer before returning.
    /// </summary>
    bool UsesBrowserDownloads { get; }

    /// <summary>
    /// Downloads a file from a URL.
    /// </summary>
    Task<DownloadResult> DownloadAsync(
        DownloadRequest request,
        IProgress<DownloadProgress> progress = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Saves content the app already holds in memory, such as a generated export.
    /// </summary>
    Task<DownloadResult> SaveAsync(string fileName, string mimeType, byte[] data);
}

/// <summary>
/// A file to download.
/// </summary>
/// <param name="Url">An absolute http or https URL.</param>
/// <param name="FileName">The name to save the file as. It is cleaned before use.</param>
/// <param name="MimeType">The content type, when known. Otherwise the response decides.</param>
/// <param name="Headers">
/// Request headers, such as authorization. Browser downloads cannot send them,
/// so a request that needs headers fails there.
/// </param>
public sealed record DownloadRequest(
    string Url,
    string FileName,
    string MimeType = null,
    IReadOnlyDictionary<string, string> Headers = null);

/// <summary>
/// How far a download has come. <paramref name="TotalBytes"/> is null when the
/// server did not say how large the file is.
/// </summary>
public readonly record struct DownloadProgress(long BytesReceived, long? TotalBytes);

/// <summary>
/// The outcome of a download.
/// </summary>
/// <param name="Success">Whether the file was saved, or handed to the browser.</param>
/// <param name="Message">What went wrong, or a short description of where the file went.</param>
/// <param name="Location">A path or name for the saved file, when the host knows one.</param>
public sealed record DownloadResult(bool Success, string Message, string Location = null)
{
    public static DownloadResult Saved(string message, string location = null) => new(true, message, location);

    public static DownloadResult Failed(string message) => new(false, message);
}
