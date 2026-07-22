using Valour.Shared;

namespace Valour.Client.Device;

/// <summary>
/// Implemented by native shells (e.g. the MAUI Android app) to expose
/// version information and update capabilities to the shared client code.
/// Web builds do not register this service.
/// </summary>
public interface INativeUpdateService
{
    /// <summary>
    /// The version of the native shell, e.g. "0.6.2".
    /// </summary>
    string CurrentVersion { get; }

    /// <summary>
    /// True when updates are delivered by an external store (e.g. Google Play),
    /// in which case the in-app update prompt should be suppressed entirely.
    /// </summary>
    bool UpdatesManagedExternally { get; }

    /// <summary>
    /// True when the shell can download and install an update package itself.
    /// </summary>
    bool CanSelfUpdate { get; }

    /// <summary>
    /// Label for the native update action, such as "Update" on Android or
    /// "Restart to update" on Windows.
    /// </summary>
    string UpdateActionLabel { get; }

    /// <summary>
    /// Downloads the update package and hands it to the OS installer.
    /// </summary>
    Task<TaskResult> DownloadAndInstallAsync(string downloadUrl);

    /// <summary>
    /// Starts the shell's external update path. This may open a release page
    /// or hand control to a platform launcher.
    /// </summary>
    Task LaunchExternalUpdateAsync(string releasePageUrl);
}
