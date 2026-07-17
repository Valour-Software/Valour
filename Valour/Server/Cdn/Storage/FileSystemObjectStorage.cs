using Valour.Shared;

namespace Valour.Server.Cdn.Storage;

/// <summary>
/// Local-disk object storage for self-hosted instances. Objects live under a
/// single root directory; keys map to relative paths. Writes are atomic
/// (temp file + move) so concurrent readers never see partial objects.
/// </summary>
public class FileSystemObjectStorage : IObjectStorage
{
    private readonly string _root;
    private readonly ILogger _logger;

    public bool SupportsSignedUrls => false;

    public FileSystemObjectStorage(string root, ILogger logger)
    {
        _root = Path.GetFullPath(root);
        _logger = logger;
        Directory.CreateDirectory(_root);
    }

    private bool TryResolve(string key, out string fullPath)
    {
        fullPath = null;

        if (string.IsNullOrWhiteSpace(key) ||
            key.Contains("..") ||
            key.Contains('\\') ||
            Path.IsPathRooted(key))
            return false;

        var candidate = Path.GetFullPath(Path.Combine(_root, key));
        if (!candidate.StartsWith(_root + Path.DirectorySeparatorChar, StringComparison.Ordinal))
            return false;

        fullPath = candidate;
        return true;
    }

    public async Task<TaskResult> PutAsync(string key, Stream data, string contentType)
    {
        if (!TryResolve(key, out var path))
            return new TaskResult(false, "Invalid storage key.");

        var temp = $"{path}.{Guid.NewGuid():N}.tmp";
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);

            await using (var file = File.Create(temp))
            {
                if (data.CanSeek)
                    data.Position = 0;

                await data.CopyToAsync(file);
            }

            File.Move(temp, path, overwrite: true);
            return TaskResult.SuccessResult;
        }
        catch (Exception e)
        {
            _logger.LogError(e, "Failed to write object {Key} to local storage", key);

            try
            {
                if (File.Exists(temp))
                    File.Delete(temp);
            }
            catch
            {
                // Best-effort temp cleanup
            }

            return new TaskResult(false, "Failed to write object to local storage.");
        }
    }

    public Task<ObjectStorageDownload> GetAsync(string key)
    {
        if (!TryResolve(key, out var path) || !File.Exists(path))
            return Task.FromResult<ObjectStorageDownload>(null);

        try
        {
            var stream = new FileStream(
                path, FileMode.Open, FileAccess.Read, FileShare.Read,
                bufferSize: 64 * 1024, useAsync: true);

            return Task.FromResult(new ObjectStorageDownload(stream));
        }
        catch (FileNotFoundException)
        {
            return Task.FromResult<ObjectStorageDownload>(null);
        }
        catch (Exception e)
        {
            _logger.LogError(e, "Failed to read object {Key} from local storage", key);
            return Task.FromResult<ObjectStorageDownload>(null);
        }
    }

    public Task<TaskResult> DeleteAsync(string key)
    {
        if (!TryResolve(key, out var path))
            return Task.FromResult(new TaskResult(false, "Invalid storage key."));

        try
        {
            if (File.Exists(path))
                File.Delete(path);

            return Task.FromResult(TaskResult.SuccessResult);
        }
        catch (Exception e)
        {
            _logger.LogError(e, "Failed to delete object {Key} from local storage", key);
            return Task.FromResult(TaskResult.FromFailure("Failed to delete object from local storage."));
        }
    }

    public Task<string> GetSignedUrlAsync(string key, string mimeType, string fileName, TimeSpan expiry)
        => Task.FromResult<string>(null);
}
