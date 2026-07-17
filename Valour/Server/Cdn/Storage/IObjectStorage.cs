using Valour.Shared;

namespace Valour.Server.Cdn.Storage;

/// <summary>
/// Backend-agnostic object storage used by the CDN layer. Implementations must
/// be safe for concurrent use. Two instances exist at runtime — one for private
/// content (message uploads, served through /content) and one for public assets
/// (avatars, icons, emoji) — resolved by <see cref="CdnStorageProvider"/>.
/// </summary>
public interface IObjectStorage
{
    /// <summary>
    /// True when GetSignedUrlAsync can produce time-limited direct URLs.
    /// When false, callers fall back to streaming through the server routes.
    /// </summary>
    bool SupportsSignedUrls { get; }

    Task<TaskResult> PutAsync(string key, Stream data, string contentType);

    /// <summary>
    /// Returns the object, or null if it does not exist. The caller owns the
    /// returned download and must dispose it (or hand its stream to a response
    /// writer that will).
    /// </summary>
    Task<ObjectStorageDownload> GetAsync(string key);

    Task<TaskResult> DeleteAsync(string key);

    /// <summary>
    /// Returns a time-limited direct URL for the object, or null when this
    /// backend does not support signing.
    /// </summary>
    Task<string> GetSignedUrlAsync(string key, string mimeType, string fileName, TimeSpan expiry);
}

/// <summary>
/// A retrieved object. Disposing releases the stream and any underlying
/// backend response it came from.
/// </summary>
public sealed class ObjectStorageDownload : IAsyncDisposable
{
    private readonly IDisposable _owner;

    public Stream Stream { get; }

    public ObjectStorageDownload(Stream stream, IDisposable owner = null)
    {
        Stream = stream;
        _owner = owner;
    }

    public async ValueTask DisposeAsync()
    {
        await Stream.DisposeAsync();
        _owner?.Dispose();
    }
}
