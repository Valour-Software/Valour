using Android.Content;
using Valour.Client.Device;
using Valour.Shared;

// Google Play does not allow apps it distributes to update themselves, so
// Play Store builds (ValourPlayStore=true) do not request the install permission.
#if !VALOUR_PLAY_STORE
[assembly: Android.App.UsesPermission(Android.Manifest.Permission.RequestInstallPackages)]
#endif

namespace Valour.Client.Maui;

/// <summary>
/// Self-update support for sideloaded (GitHub release) Android builds.
/// Downloads the release APK and hands it to the system package installer.
/// Suppressed entirely for Play Store builds and installs, which Play keeps updated.
/// </summary>
public class AndroidUpdateService : INativeUpdateService
{
    private const string PlayStorePackage = "com.android.vending";
    private const string UpdateFileName = "valour-update.apk";

    private static readonly HttpClient DownloadClient = new();

    public string CurrentVersion => AppInfo.Current.VersionString;

#if VALOUR_PLAY_STORE
    public bool UpdatesManagedExternally => true;

    public bool CanSelfUpdate => false;
#else
    public bool UpdatesManagedExternally => IsPlayInstalled();

    public bool CanSelfUpdate => !IsPlayInstalled();
#endif

    public string UpdateActionLabel => "Update";

    public async Task<TaskResult> DownloadAndInstallAsync(string downloadUrl)
    {
        try
        {
            var context = Android.App.Application.Context;

            var updateDir = new Java.IO.File(context.ExternalCacheDir ?? context.CacheDir!, "updates");
            if (!updateDir.Exists())
                updateDir.Mkdirs();

            var apkFile = new Java.IO.File(updateDir, UpdateFileName);
            if (apkFile.Exists())
                apkFile.Delete();

            using (var response = await DownloadClient.GetAsync(downloadUrl, HttpCompletionOption.ResponseHeadersRead))
            {
                if (!response.IsSuccessStatusCode)
                    return new TaskResult(false, $"Download failed ({(int)response.StatusCode})");

                await using var fileStream = File.Create(apkFile.AbsolutePath);
                await response.Content.CopyToAsync(fileStream);
            }

            var apkUri = AndroidX.Core.Content.FileProvider.GetUriForFile(
                context,
                $"{context.PackageName}.updates.fileprovider",
                apkFile);

            var intent = new Intent(Intent.ActionView);
            intent.SetDataAndType(apkUri, "application/vnd.android.package-archive");
            intent.AddFlags(ActivityFlags.GrantReadUriPermission | ActivityFlags.NewTask);

            // Prefer the foreground activity so the installer opens on top of the app
            var activity = Platform.CurrentActivity;
            if (activity is not null)
                activity.StartActivity(intent);
            else
                context.StartActivity(intent);

            return new TaskResult(true, "Installer launched");
        }
        catch (Exception ex)
        {
            return new TaskResult(false, $"Update failed: {ex.Message}");
        }
    }

    public async Task LaunchExternalUpdateAsync(string releasePageUrl)
    {
        await Browser.Default.OpenAsync(releasePageUrl, BrowserLaunchMode.External);
    }

    private static bool IsPlayInstalled()
    {
        try
        {
            var context = Android.App.Application.Context;
            var packageManager = context.PackageManager;
            if (packageManager is null || context.PackageName is null)
                return false;

            string? installer;
            if (OperatingSystem.IsAndroidVersionAtLeast(30))
            {
                installer = packageManager.GetInstallSourceInfo(context.PackageName).InstallingPackageName;
            }
            else
            {
#pragma warning disable CA1422
                installer = packageManager.GetInstallerPackageName(context.PackageName);
#pragma warning restore CA1422
            }

            return installer == PlayStorePackage;
        }
        catch
        {
            return false;
        }
    }
}
