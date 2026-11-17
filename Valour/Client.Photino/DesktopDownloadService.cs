using Valour.Client.Utility.Downloads;

namespace Valour.Client.Photino;

/// <summary>
/// Saves downloads to the person's Downloads folder. The Linux web view does not
/// handle download links on its own, so the app downloads files itself.
/// </summary>
public sealed class DesktopDownloadService : StreamedDownloadService
{
    protected override Task<DownloadDestination> OpenDestinationAsync(
        string fileName, string mimeType, CancellationToken cancellationToken) =>
        Task.FromResult<DownloadDestination>(new FolderDownloadDestination(DownloadsFolder(), fileName));

    /// <summary>
    /// The folder the person chose for downloads. Linux desktops record it in
    /// <c>user-dirs.dirs</c>; it is ~/Downloads unless they moved it.
    /// </summary>
    internal static string DownloadsFolder()
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var fallback = Path.Combine(home, "Downloads");

        try
        {
            var configHome = Environment.GetEnvironmentVariable("XDG_CONFIG_HOME");
            if (string.IsNullOrWhiteSpace(configHome))
                configHome = Path.Combine(home, ".config");

            var file = Path.Combine(configHome, "user-dirs.dirs");
            if (File.Exists(file))
            {
                foreach (var line in File.ReadLines(file))
                {
                    var parsed = ParseDownloadsLine(line, home);
                    if (parsed is not null)
                        return parsed;
                }
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }

        return fallback;
    }

    /// <summary>
    /// Reads a line such as <c>XDG_DOWNLOAD_DIR="$HOME/Downloads"</c>. Returns
    /// null for any other line, or for a folder outside the person's home.
    /// </summary>
    internal static string ParseDownloadsLine(string line, string home)
    {
        const string key = "XDG_DOWNLOAD_DIR=";

        var trimmed = line.Trim();
        if (!trimmed.StartsWith(key, StringComparison.Ordinal))
            return null;

        var value = trimmed[key.Length..].Trim().Trim('"');
        if (value.StartsWith("$HOME", StringComparison.Ordinal))
            value = home + value["$HOME".Length..];

        return Path.IsPathRooted(value) && value.StartsWith(home, StringComparison.Ordinal) ? value : null;
    }
}
