using System.Diagnostics;
using System.Text;
using Valour.Client.Device;
using Valour.Shared;

namespace Valour.Client.Maui;

/// <summary>
/// Restarts the unpackaged Windows app through its cached launcher. The
/// launcher owns download, validation, installation, and rollback behavior.
/// </summary>
public sealed class WindowsUpdateService : INativeUpdateService
{
    private const string InstalledLauncherFileName = "ValourLauncher.exe";

    // Release builds place the signed launcher next to Valour.exe under this
    // name. Development builds do not include it.
    private const string BundledLauncherFileName = "ValourLauncher.exe";

    // The launcher that started the app may still be exiting, which keeps its
    // file locked for a moment.
    private static readonly TimeSpan LauncherRefreshDelay = TimeSpan.FromSeconds(10);

    // Launchers from before 0.9.9 cache the app inside "Valour-full.exe"
    // instead of installing a launcher copy. Running that file starts the
    // current launcher code, which then installs its own copy.
    private const string LegacyReleaseExecutableName = "Valour-full.exe";
    private const string LegacyLatestTagFileName = "latest-release-tag.txt";

    public string CurrentVersion => AppInfo.Current.VersionString;

    public bool UpdatesManagedExternally => false;

    public bool CanSelfUpdate => false;

    public string UpdateActionLabel => "Restart to update";

    public Task<TaskResult> DownloadAndInstallAsync(string downloadUrl) =>
        Task.FromResult(new TaskResult(false, "Windows updates are installed by the Valour launcher."));

    public async Task LaunchExternalUpdateAsync(string releasePageUrl)
    {
        var launcherPath = FindCachedLauncher();
        if (launcherPath is null)
        {
            await Browser.Default.OpenAsync(releasePageUrl, BrowserLaunchMode.External);
            return;
        }

        var launcher = Process.Start(new ProcessStartInfo(launcherPath)
        {
            UseShellExecute = true,
            WorkingDirectory = Path.GetDirectoryName(launcherPath)!
        });

        if (launcher is null)
        {
            await Browser.Default.OpenAsync(releasePageUrl, BrowserLaunchMode.External);
            return;
        }

        // Give Windows a moment to create the launcher process before releasing
        // the app's single-instance mutex and terminating this process.
        await Task.Delay(150);

        if (Microsoft.UI.Xaml.Application.Current is WinUI.App app)
        {
            app.ExitForUpdate();
            return;
        }

        Environment.Exit(0);
    }

    /// <summary>
    /// Replaces the installed launcher copy with the launcher bundled in this
    /// release when the bundled one is newer, or installs it when no copy
    /// exists. A launcher never replaces itself with a newer release, so this
    /// is how launcher changes reach existing installs. Runs in the background
    /// and ignores failures; the next start tries again.
    /// </summary>
    public static void RefreshInstalledLauncherInBackground()
    {
        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(LauncherRefreshDelay);
                RefreshInstalledLauncher();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Could not refresh the Valour launcher: {ex.Message}");
            }
        });
    }

    private static void RefreshInstalledLauncher()
    {
        var bundledLauncher = Path.Combine(AppContext.BaseDirectory, BundledLauncherFileName);
        if (!File.Exists(bundledLauncher))
            return;

        var launcherRoot = GetLauncherRoot();
        var installedLauncher = Path.Combine(launcherRoot, InstalledLauncherFileName);
        if (File.Exists(installedLauncher) && GetFileVersion(installedLauncher) >= GetFileVersion(bundledLauncher))
            return;

        Directory.CreateDirectory(launcherRoot);
        var tempPath = installedLauncher + ".tmp-" + Guid.NewGuid().ToString("N");
        try
        {
            File.Copy(bundledLauncher, tempPath);
            File.Move(tempPath, installedLauncher, overwrite: true);
        }
        finally
        {
            if (File.Exists(tempPath))
                File.Delete(tempPath);
        }
    }

    private static Version GetFileVersion(string path)
    {
        var info = FileVersionInfo.GetVersionInfo(path);
        return new Version(info.FileMajorPart, info.FileMinorPart, info.FileBuildPart, info.FilePrivatePart);
    }

    private static string GetLauncherRoot() => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Valour",
        "Launcher");

    private static string? FindCachedLauncher()
    {
        try
        {
            var launcherRoot = GetLauncherRoot();

            var installedLauncher = Path.Combine(launcherRoot, InstalledLauncherFileName);
            if (File.Exists(installedLauncher))
                return installedLauncher;

            var releasesRoot = Path.Combine(launcherRoot, "releases");

            var latestTagPath = Path.Combine(launcherRoot, LegacyLatestTagFileName);
            if (File.Exists(latestTagPath))
            {
                var latestTag = File.ReadAllText(latestTagPath).Trim();
                if (!string.IsNullOrWhiteSpace(latestTag))
                {
                    var taggedLauncher = Path.Combine(
                        releasesRoot,
                        SanitizePathSegment(latestTag),
                        LegacyReleaseExecutableName);
                    if (File.Exists(taggedLauncher))
                        return taggedLauncher;
                }
            }

            if (!Directory.Exists(releasesRoot))
                return null;

            return Directory
                .EnumerateFiles(releasesRoot, LegacyReleaseExecutableName, SearchOption.AllDirectories)
                .OrderByDescending(File.GetLastWriteTimeUtc)
                .FirstOrDefault();
        }
        catch
        {
            return null;
        }
    }

    private static string SanitizePathSegment(string value)
    {
        var invalidChars = Path.GetInvalidFileNameChars();
        var builder = new StringBuilder(value.Length);

        foreach (var c in value)
            builder.Append(Array.IndexOf(invalidChars, c) >= 0 ? '_' : c);

        return builder.Length == 0 ? "unknown" : builder.ToString();
    }
}
