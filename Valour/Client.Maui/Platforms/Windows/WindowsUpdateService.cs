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
    private const string ReleaseExecutableName = "Valour-full.exe";
    private const string LatestTagFileName = "latest-release-tag.txt";

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

    private static string? FindCachedLauncher()
    {
        try
        {
            var launcherRoot = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Valour",
                "Launcher");
            var releasesRoot = Path.Combine(launcherRoot, "releases");

            var latestTagPath = Path.Combine(launcherRoot, LatestTagFileName);
            if (File.Exists(latestTagPath))
            {
                var latestTag = File.ReadAllText(latestTagPath).Trim();
                if (!string.IsNullOrWhiteSpace(latestTag))
                {
                    var taggedLauncher = Path.Combine(
                        releasesRoot,
                        SanitizePathSegment(latestTag),
                        ReleaseExecutableName);
                    if (File.Exists(taggedLauncher))
                        return taggedLauncher;
                }
            }

            if (!Directory.Exists(releasesRoot))
                return null;

            return Directory
                .EnumerateFiles(releasesRoot, ReleaseExecutableName, SearchOption.AllDirectories)
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
