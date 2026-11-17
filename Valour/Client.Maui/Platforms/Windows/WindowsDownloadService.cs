using Valour.Client.Utility.Downloads;

namespace Valour.Client.Maui;

/// <summary>
/// Saves downloads to the person's Downloads folder, so the app knows when a
/// file is complete and can say where it went.
/// </summary>
public sealed class WindowsDownloadService : StreamedDownloadService
{
    protected override Task<DownloadDestination> OpenDestinationAsync(
        string fileName, string mimeType, CancellationToken cancellationToken)
    {
        string folder;
        try
        {
            // Follows the folder the person moved Downloads to, if they did.
            folder = global::Windows.Storage.UserDataPaths.GetDefault().Downloads;
        }
        catch (Exception)
        {
            folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
        }

        return Task.FromResult<DownloadDestination>(new FolderDownloadDestination(folder, fileName));
    }
}
