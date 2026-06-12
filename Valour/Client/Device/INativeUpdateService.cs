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
    /// Downloads the update package and hands it to the OS installer.
    /// </summary>
    Task<TaskResult> DownloadAndInstallAsync(string downloadUrl);

    /// <summary>
    /// Opens the release page in the system browser as a fallback
    /// for shells that cannot self-update.
    /// </summary>
    Task OpenReleasePageAsync(string url);
}
