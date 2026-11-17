namespace Valour.Client.Utility.Downloads;

/// <summary>
/// Where a download is being written. A host provides one for each file: a file
/// in the Downloads folder, an entry in Android's shared Downloads, or a
/// temporary file that is offered to the person afterward.
/// </summary>
public abstract class DownloadDestination : IAsyncDisposable
{
    /// <summary>
    /// The stream the downloaded bytes are written to.
    /// </summary>
    public abstract Stream Stream { get; }

    /// <summary>
    /// Called after every byte was written. Closes the stream and makes the file
    /// available to the person. Returns a path or name describing where it went.
    /// </summary>
    public abstract Task<string> CompleteAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Called when the download failed or was cancelled. Closes the stream and
    /// removes the partial file.
    /// </summary>
    public abstract Task AbortAsync();

    public virtual ValueTask DisposeAsync() => Stream.DisposeAsync();
}

/// <summary>
/// A destination that is a new file in a folder. Hosts whose downloads end up as
/// plain files use this directly or derive from it.
/// </summary>
public class FolderDownloadDestination : DownloadDestination
{
    private readonly FileStream _stream;
    private readonly string _path;
    private bool _finished;

    public FolderDownloadDestination(string directory, string fileName)
    {
        _stream = DownloadFileNames.CreateUnique(directory, fileName, out _path);
    }

    public override Stream Stream => _stream;

    protected string Path => _path;

    public override async Task<string> CompleteAsync(CancellationToken cancellationToken)
    {
        await _stream.FlushAsync(cancellationToken);
        await _stream.DisposeAsync();
        _finished = true;
        return _path;
    }

    public override async Task AbortAsync()
    {
        await _stream.DisposeAsync();
        _finished = true;

        try
        {
            File.Delete(_path);
        }
        catch (IOException)
        {
            // A partial file that cannot be removed is better than a crash.
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    public override async ValueTask DisposeAsync()
    {
        // A destination that was neither completed nor aborted was abandoned.
        if (!_finished)
            await AbortAsync();
    }
}
