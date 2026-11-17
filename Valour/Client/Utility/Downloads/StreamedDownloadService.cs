namespace Valour.Client.Utility.Downloads;

/// <summary>
/// The download service for hosts that can write files themselves. It streams
/// the response to a destination without holding the file in memory, so large
/// files, such as an attachment archive, are safe to download. A host supplies
/// only <see cref="OpenDestinationAsync"/>.
/// </summary>
public abstract class StreamedDownloadService : IDownloadService
{
    // Downloads can take as long as the person's connection needs, so the client
    // has no overall timeout. Cancellation is the way to stop one. The client
    // is separate from the app's own, so it never sends the person's token.
    private static readonly HttpClient SharedHttp = new() { Timeout = Timeout.InfiniteTimeSpan };

    private static readonly TimeSpan ProgressInterval = TimeSpan.FromMilliseconds(150);

    private readonly HttpClient _http;

    protected StreamedDownloadService(HttpClient http = null)
    {
        _http = http ?? SharedHttp;
    }

    public bool UsesBrowserDownloads => false;

    /// <summary>
    /// Opens the place the file will be written. <paramref name="fileName"/> is
    /// already cleaned.
    /// </summary>
    protected abstract Task<DownloadDestination> OpenDestinationAsync(
        string fileName, string mimeType, CancellationToken cancellationToken);

    public async Task<DownloadResult> DownloadAsync(
        DownloadRequest request,
        IProgress<DownloadProgress> progress = null,
        CancellationToken cancellationToken = default)
    {
        if (request is null || !Uri.TryCreate(request.Url, UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            return DownloadResult.Failed("This download link is not valid.");
        }

        var fileName = DownloadFileNames.Sanitize(request.FileName);

        try
        {
            using var message = new HttpRequestMessage(HttpMethod.Get, uri);
            if (request.Headers is not null)
            {
                foreach (var (name, value) in request.Headers)
                    message.Headers.TryAddWithoutValidation(name, value);
            }

            using var response = await _http.SendAsync(message, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            if (!response.IsSuccessStatusCode)
                return DownloadResult.Failed($"The server answered {(int)response.StatusCode}.");

            var mimeType = request.MimeType ?? response.Content.Headers.ContentType?.MediaType;
            await using var source = await response.Content.ReadAsStreamAsync(cancellationToken);

            return await WriteAsync(fileName, mimeType, source, response.Content.Headers.ContentLength, progress, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            return DownloadResult.Failed("The download was cancelled.");
        }
        catch (HttpRequestException)
        {
            return DownloadResult.Failed("The download could not be reached. Check your connection and try again.");
        }
        catch (IOException ex)
        {
            return DownloadResult.Failed("The file could not be saved: " + ex.Message);
        }
        catch (UnauthorizedAccessException)
        {
            return DownloadResult.Failed("This app is not allowed to save files there.");
        }
    }

    public async Task<DownloadResult> SaveAsync(string fileName, string mimeType, byte[] data)
    {
        try
        {
            await using var source = new MemoryStream(data ?? [], writable: false);
            return await WriteAsync(DownloadFileNames.Sanitize(fileName), mimeType, source, source.Length, null, default);
        }
        catch (IOException ex)
        {
            return DownloadResult.Failed("The file could not be saved: " + ex.Message);
        }
        catch (UnauthorizedAccessException)
        {
            return DownloadResult.Failed("This app is not allowed to save files there.");
        }
    }

    private async Task<DownloadResult> WriteAsync(
        string fileName,
        string mimeType,
        Stream source,
        long? totalBytes,
        IProgress<DownloadProgress> progress,
        CancellationToken cancellationToken)
    {
        await using var destination = await OpenDestinationAsync(fileName, mimeType, cancellationToken);

        try
        {
            var buffer = new byte[81920];
            long received = 0;
            var lastReport = DateTime.UtcNow;

            int read;
            while ((read = await source.ReadAsync(buffer, cancellationToken)) > 0)
            {
                await destination.Stream.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
                received += read;

                if (progress is not null && DateTime.UtcNow - lastReport >= ProgressInterval)
                {
                    progress.Report(new DownloadProgress(received, totalBytes));
                    lastReport = DateTime.UtcNow;
                }
            }

            // A connection that drops mid-file can look like a normal end.
            if (totalBytes is { } expected && received != expected)
                throw new IOException("The download ended early.");

            progress?.Report(new DownloadProgress(received, totalBytes));

            var location = await destination.CompleteAsync(cancellationToken);
            return DownloadResult.Saved(location is null ? "Saved " + fileName : "Saved to " + location, location);
        }
        catch
        {
            await destination.AbortAsync();
            throw;
        }
    }
}
