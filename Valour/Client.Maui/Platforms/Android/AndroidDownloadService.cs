using Android.Content;
using Android.OS;
using Valour.Client.Utility.Downloads;

namespace Valour.Client.Maui;

/// <summary>
/// Saves downloads to the shared Downloads collection, where the person finds
/// them in the Files app. Android's WebView ignores download links, so the app
/// downloads files itself. Android 10 and newer need no storage permission for
/// this. Older versions save to the app's own Downloads folder, which also
/// needs no permission but is less convenient to reach.
/// </summary>
public sealed class AndroidDownloadService : StreamedDownloadService
{
    protected override Task<DownloadDestination> OpenDestinationAsync(
        string fileName, string mimeType, CancellationToken cancellationToken)
    {
        if (Build.VERSION.SdkInt >= BuildVersionCodes.Q)
            return Task.FromResult<DownloadDestination>(new MediaStoreDestination(fileName, mimeType));

        var folder = Android.App.Application.Context.GetExternalFilesDir(Android.OS.Environment.DirectoryDownloads)?.AbsolutePath
                     ?? Path.Combine(Path.GetTempPath(), "valour-downloads");
        return Task.FromResult<DownloadDestination>(new FolderDownloadDestination(folder, fileName));
    }

    /// <summary>
    /// A new entry in MediaStore's Downloads. It stays hidden from other apps
    /// until the whole file is written, and MediaStore renames it when the name
    /// is already taken.
    /// </summary>
    private sealed class MediaStoreDestination : DownloadDestination
    {
        // MediaStore.Downloads.EXTERNAL_CONTENT_URI.
        private static readonly Android.Net.Uri DownloadsUri = Android.Net.Uri.Parse("content://media/external/downloads")!;

        private readonly ContentResolver _resolver;
        private readonly Android.Net.Uri _uri;
        private readonly Stream _stream;
        private readonly string _fileName;
        private bool _finished;

        public MediaStoreDestination(string fileName, string mimeType)
        {
            _fileName = fileName;
            _resolver = Android.App.Application.Context.ContentResolver
                        ?? throw new IOException("Android did not provide a content resolver.");

            using var values = new ContentValues();
            values.Put("_display_name", fileName);
            values.Put("mime_type", string.IsNullOrWhiteSpace(mimeType) ? "application/octet-stream" : mimeType);
            values.Put("relative_path", Android.OS.Environment.DirectoryDownloads);
            values.Put("is_pending", 1);

            _uri = _resolver.Insert(DownloadsUri, values)
                   ?? throw new IOException("Android would not create the file in Downloads.");

            try
            {
                _stream = _resolver.OpenOutputStream(_uri, "w")
                          ?? throw new IOException("Android would not open the file in Downloads.");
            }
            catch
            {
                _resolver.Delete(_uri, null, null);
                throw;
            }
        }

        public override Stream Stream => _stream;

        public override async Task<string> CompleteAsync(CancellationToken cancellationToken)
        {
            await _stream.FlushAsync(cancellationToken);
            await _stream.DisposeAsync();
            _finished = true;

            using var values = new ContentValues();
            values.Put("is_pending", 0);
            _resolver.Update(_uri, values, null, null);

            return Path.Combine(Android.OS.Environment.DirectoryDownloads, _fileName);
        }

        public override async Task AbortAsync()
        {
            await _stream.DisposeAsync();
            _finished = true;
            _resolver.Delete(_uri, null, null);
        }

        public override async ValueTask DisposeAsync()
        {
            if (!_finished)
                await AbortAsync();
        }
    }
}
