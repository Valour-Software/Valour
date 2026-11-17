# Downloads

Anything the app saves to the person's device goes through `IDownloadService`
(`Valour/Client/Utility/Downloads`). Hosts save files in different ways, and calling
the service keeps each feature from handling them itself.

## Using it

```csharp
@inject IDownloadService Downloads

// A file on a server.
await Downloads.DownloadWithToastAsync(new DownloadRequest(url, "report.zip", "application/zip"));

// Content the app already has in memory.
await Downloads.SaveWithToastAsync("event.ics", "text/calendar", bytes);
```

`DownloadAsync` and `SaveAsync` return a `DownloadResult` and show nothing. The
`...WithToastAsync` versions also tell the person how it went. They announce failures
everywhere and confirm success only where the browser's own download manager is not
doing that.

`DownloadAsync` accepts an `IProgress<DownloadProgress>`. Report progress only where
`UsesBrowserDownloads` is false, because a browser does not tell the app how far a
download has come.

File names are cleaned before use. Folders, control characters, characters that some
file systems refuse, and reserved Windows names are removed or replaced. A download
never replaces a file that already exists. It adds " (1)", " (2)" and so on.

## What each host does

| Host | Implementation | Result |
| --- | --- | --- |
| Browser | `BrowserDownloadService` | The browser's download manager saves the file. |
| Windows app | `WindowsDownloadService` | Saves to the Downloads folder. |
| Android app | `AndroidDownloadService` | Saves to the shared Downloads collection. Before Android 10 it saves to the app's own Downloads folder. |
| Linux app | `DesktopDownloadService` | Saves to the folder in `user-dirs.dirs`, normally `~/Downloads`. |
| iOS and Mac app | `BrowserDownloadService` | `AppleWebViewNavigationDelegate` turns download links into the system Save panel. |

`UsesBrowserDownloads` is true for the browser and the Apple apps. The call then returns
as soon as the download has been started, and the app cannot see it finish. A feature
that must know the file arrived, such as the attachment backup before a deletion, asks
the server. Where it is false the call returns after the file is saved.

A browser cannot send request headers, so a request with headers fails there. Give the
browser a URL that authorizes itself, such as one with a signed or single-use id.

## Adding a host

Derive from `StreamedDownloadService` and implement `OpenDestinationAsync`. The base class
streams the response to the destination without holding the file in memory, reports
progress, checks that the whole file arrived, and removes a partial file when a download
fails or is cancelled. `FolderDownloadDestination` covers hosts that save plain files. Register
the new service after `AddValourClientServices`.

## Tests

`Valour/Tests/Client/DownloadServiceTests.cs` covers streaming, progress, existing files,
truncated and refused downloads, cancellation, and file names.
`AttachmentStorageLiveTests` downloads a real archive the way a native host does.
