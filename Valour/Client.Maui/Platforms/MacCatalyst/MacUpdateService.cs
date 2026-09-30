using Valour.Client.Device;
using Valour.Shared;

namespace Valour.Client.Maui;

/// <summary>
/// The Mac app is downloaded from the GitHub release page and replaced by
/// dragging the new copy into Applications, so the update action opens that
/// page in the browser.
/// </summary>
public sealed class MacUpdateService : INativeUpdateService
{
    public string CurrentVersion => AppInfo.Current.VersionString;

    public bool UpdatesManagedExternally => false;

    public bool CanSelfUpdate => false;

    public string UpdateActionLabel => "Download update";

    public Task<TaskResult> DownloadAndInstallAsync(string downloadUrl) =>
        Task.FromResult(new TaskResult(false, "Mac updates are downloaded from the release page."));

    public Task LaunchExternalUpdateAsync(string releasePageUrl) =>
        Browser.Default.OpenAsync(releasePageUrl, BrowserLaunchMode.External);
}
