using System.Collections.Concurrent;

namespace Valour.Sdk.E2ee;

/// <summary>
/// Stores this device's private keys. Keys never leave the device, so losing
/// this storage means the device must be linked again or restored with a
/// recovery code.
///
/// Apps provide a store backed by the platform: browser storage on the web,
/// the system keychain or keystore on phones and desktops. Bots default to
/// <see cref="FileE2eeKeyStore"/>.
/// </summary>
public interface IE2eeKeyStore
{
    Task<byte[]> GetAsync(string key);
    Task SetAsync(string key, byte[] value);
    Task RemoveAsync(string key);
}

/// <summary>
/// Keeps keys in memory only. Everything is lost when the process exits, so
/// this suits tests and short-lived tools.
/// </summary>
public sealed class MemoryE2eeKeyStore : IE2eeKeyStore
{
    private readonly ConcurrentDictionary<string, byte[]> _values = new();

    public Task<byte[]> GetAsync(string key) =>
        Task.FromResult(_values.TryGetValue(key, out var value) ? value : null);

    public Task SetAsync(string key, byte[] value)
    {
        _values[key] = value;
        return Task.CompletedTask;
    }

    public Task RemoveAsync(string key)
    {
        _values.TryRemove(key, out _);
        return Task.CompletedTask;
    }
}

/// <summary>
/// Keeps keys as files in a directory. Protect the directory the way you would
/// protect the bot's token: anyone who can read it can read the bot's messages.
/// On Linux and macOS the directory is readable only by its owner (mode 0700)
/// and each file is too (mode 0600).
///
/// The directory must survive restarts. A bot in a container needs it on a
/// persistent volume; otherwise every restart starts without keys.
/// </summary>
public sealed class FileE2eeKeyStore : IE2eeKeyStore
{
    private const UnixFileMode KeyDirectoryMode = UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute;
    private const UnixFileMode KeyFileMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;

    private readonly string _directory;

    public FileE2eeKeyStore(string directory)
    {
        _directory = Path.GetFullPath(directory);
        CreatedDirectory = !Directory.Exists(_directory);

        if (OperatingSystem.IsWindows() || OperatingSystem.IsBrowser())
        {
            Directory.CreateDirectory(_directory);
        }
        else
        {
            Directory.CreateDirectory(_directory, KeyDirectoryMode);

            // A directory that already existed keeps its mode, so it is
            // tightened here too.
            try
            {
                File.SetUnixFileMode(_directory, KeyDirectoryMode);
            }
            catch (Exception e) when (e is UnauthorizedAccessException or IOException)
            {
                // Owned by another user; the files are still created 0600.
            }
        }
    }

    public static string DefaultDirectory => Path.Combine(Environment.CurrentDirectory, ".valour-e2ee");

    /// <summary>The full path of the directory holding the keys.</summary>
    public string DirectoryPath => _directory;

    /// <summary>
    /// True when this store created its directory, which means no keys were
    /// kept from an earlier run.
    /// </summary>
    public bool CreatedDirectory { get; }

    public async Task<byte[]> GetAsync(string key)
    {
        var path = PathFor(key);
        return File.Exists(path) ? await File.ReadAllBytesAsync(path) : null;
    }

    public async Task SetAsync(string key, byte[] value)
    {
        // Write to a uniquely named temporary file and flush it to disk before
        // renaming it into place, so a crash cannot leave a torn key and two
        // writers cannot collide on the temporary file.
        var path = PathFor(key);
        var temp = $"{path}.{Guid.NewGuid():N}.tmp";
        var options = new FileStreamOptions
        {
            Mode = FileMode.CreateNew,
            Access = FileAccess.Write,
            Share = FileShare.None
        };
        if (!OperatingSystem.IsWindows() && !OperatingSystem.IsBrowser())
            options.UnixCreateMode = KeyFileMode;

        try
        {
            await using (var stream = new FileStream(temp, options))
            {
                await stream.WriteAsync(value);
                await stream.FlushAsync();
                stream.Flush(flushToDisk: true);
            }

            File.Move(temp, path, overwrite: true);
        }
        catch
        {
            try
            {
                File.Delete(temp);
            }
            catch (IOException)
            {
                // Nothing more can be done; the rename is the only step that matters.
            }

            throw;
        }
    }

    public Task RemoveAsync(string key)
    {
        var path = PathFor(key);
        if (File.Exists(path))
            File.Delete(path);
        return Task.CompletedTask;
    }

    private string PathFor(string key) =>
        Path.Combine(_directory, Base64Url.Encode(E2eeCrypto.Utf8(key)) + ".key");
}
