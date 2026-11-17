namespace Valour.Server.Services.GoogleDrive;

/// <summary>Why a Google Drive call failed, so callers can decide whether to retry.</summary>
public enum DriveErrorKind
{
    /// <summary>Google no longer accepts the account's access. The person has to reconnect.</summary>
    AccessRevoked,

    /// <summary>The file or folder is gone, or the person moved it to the trash.</summary>
    NotFound,

    /// <summary>The person's Drive has no room left.</summary>
    DriveFull,

    /// <summary>Google asked us to slow down, or had a temporary problem. Try again later.</summary>
    Transient,

    Other,
}

public class DriveApiException : Exception
{
    public DriveErrorKind Kind { get; }

    public DriveApiException(DriveErrorKind kind, string message, Exception inner = null)
        : base(message, inner)
    {
        Kind = kind;
    }
}

/// <summary>A short-lived access token for one account.</summary>
public record DriveAccessToken(string Token, DateTime ExpiresAt);

/// <summary>A Drive file's download, with the connection it came from.</summary>
public sealed class DriveDownload : IAsyncDisposable
{
    private readonly IDisposable _owner;

    public Stream Stream { get; }
    public long? Length { get; }

    public DriveDownload(Stream stream, long? length, IDisposable owner = null)
    {
        Stream = stream;
        Length = length;
        _owner = owner;
    }

    public async ValueTask DisposeAsync()
    {
        await Stream.DisposeAsync();
        _owner?.Dispose();
    }
}

/// <summary>
/// The parts of the Google Drive API that attachment storage uses. Valour only
/// holds the drive.file permission, so every call reaches files and folders
/// Valour made. Tests replace this with an in-memory Drive.
/// </summary>
public interface IGoogleDriveApi
{
    /// <summary>
    /// Trades a refresh token for an access token. Throws
    /// <see cref="DriveErrorKind.AccessRevoked"/> when Google refuses it.
    /// </summary>
    Task<DriveAccessToken> RefreshAccessTokenAsync(string refreshToken, CancellationToken cancellationToken = default);

    /// <summary>Creates a folder and returns its ID. A null parent is the top of My Drive.</summary>
    Task<string> CreateFolderAsync(string accessToken, string name, string parentId, CancellationToken cancellationToken = default);

    /// <summary>Whether the folder still exists and is not in the trash.</summary>
    Task<bool> FolderExistsAsync(string accessToken, string folderId, CancellationToken cancellationToken = default);

    Task RenameAsync(string accessToken, string fileId, string name, CancellationToken cancellationToken = default);

    /// <summary>Uploads a file and returns its ID.</summary>
    Task<string> UploadFileAsync(
        string accessToken,
        string parentId,
        string name,
        string mimeType,
        Stream content,
        long length,
        IReadOnlyDictionary<string, string> properties,
        CancellationToken cancellationToken = default);

    /// <summary>Opens a file's contents. Throws <see cref="DriveErrorKind.NotFound"/> when it is gone.</summary>
    Task<DriveDownload> OpenDownloadAsync(string accessToken, string fileId, CancellationToken cancellationToken = default);

    /// <summary>Tells Google to forget the grant. Failures are not reported.</summary>
    Task RevokeAsync(string token);
}
