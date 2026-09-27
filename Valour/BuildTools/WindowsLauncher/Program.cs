using System.Diagnostics;
using System.IO.Compression;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Windows.Forms;

namespace Valour.WindowsLauncher;

/// <summary>
/// Downloads the latest Windows app archive from GitHub releases, installs it
/// under %LocalAppData%\Valour\Launcher\app, and starts it. The launcher also
/// keeps a copy of itself at a fixed path so the app can restart through it to
/// apply updates.
/// </summary>
internal static class Program
{
    private const string LatestReleaseApiUrl = "https://api.github.com/repos/Valour-Software/Valour/releases/latest";
    private const string AppArchiveAssetName = "Valour-windows-x64.zip";
    private const string AppExecutableName = "Valour.exe";
    private const string InstalledMarkerFileName = ".installed";
    private const string CurrentTagFileName = "current-app-tag.txt";
    private const string InstalledLauncherFileName = "ValourLauncher.exe";

    // Earlier launchers shipped the app inside "Valour-full.exe" and installed
    // it to versions\<payload hash>. Those launchers are still in use, so the
    // format is still published, and its installs serve as an offline fallback.
    private const string LegacyInstallDirectoryName = "versions";
    private const string LegacyInstalledMarkerFileName = ".payload";
    private static readonly byte[] LegacyPayloadMarker = Encoding.ASCII.GetBytes("VALOURP1");

    private static readonly HttpClient GitHubClient = CreateGitHubClient();

    [STAThread]
    private static int Main(string[] args)
    {
        var exitCode = 1;

        try
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            using var statusWindow = new LauncherStatusWindow();
            statusWindow.Shown += (_, _) =>
            {
                _ = Task.Run(async () =>
                {
                    try
                    {
                        exitCode = await RunLauncherAsync(args, statusWindow).ConfigureAwait(false);
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine(ex);
                        statusWindow.SetStatus(GetFailureMessage(ex));
                        await Task.Delay(1400).ConfigureAwait(false);
                        exitCode = 1;
                    }
                    finally
                    {
                        statusWindow.SafeClose();
                    }
                });
            };

            Application.Run(statusWindow);
        }
        catch (Exception ex)
        {
            Debug.WriteLine(ex);
            exitCode = 1;
        }

        return exitCode;
    }

    private static async Task<int> RunLauncherAsync(string[] args, LauncherStatusWindow statusWindow)
    {
        var launcherPath = Environment.ProcessPath
            ?? throw new InvalidOperationException("Unable to resolve launcher path.");

        var launcherRoot = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Valour",
            "Launcher");
        Directory.CreateDirectory(launcherRoot);

        TryInstallLauncherCopy(launcherPath, Path.Combine(launcherRoot, InstalledLauncherFileName));

        statusWindow.SetStatus("Checking for updates...");
        var appPath = await ResolveAppPathAsync(launcherPath, launcherRoot, statusWindow).ConfigureAwait(false);

        statusWindow.SetStatus("Launching Valour...");
        var psi = new ProcessStartInfo(appPath)
        {
            UseShellExecute = false,
            WorkingDirectory = Path.GetDirectoryName(appPath)!
        };

        foreach (var arg in args)
        {
            psi.ArgumentList.Add(arg);
        }

        Process.Start(psi);
        return 0;
    }

    private static async Task<string> ResolveAppPathAsync(
        string launcherPath,
        string launcherRoot,
        LauncherStatusWindow statusWindow)
    {
        var appRoot = Path.Combine(launcherRoot, "app");
        Directory.CreateDirectory(appRoot);
        var currentTagPath = Path.Combine(launcherRoot, CurrentTagFileName);

        try
        {
            var latestRelease = await FetchLatestReleaseAssetAsync().ConfigureAwait(false);
            if (latestRelease is null)
            {
                return FindFallbackAppPath(launcherRoot, appRoot, currentTagPath, "Could not check updates. Launching installed version.", statusWindow);
            }

            var installDir = Path.Combine(appRoot, SanitizePathSegment(latestRelease.Tag));
            if (!IsInstalled(installDir))
            {
                await DownloadAndInstallAsync(latestRelease, launcherPath, appRoot, installDir, statusWindow)
                    .ConfigureAwait(false);
            }
            else
            {
                statusWindow.SetStatus("Already up to date.", 100);
            }

            TryWriteText(currentTagPath, latestRelease.Tag);
            CleanupOldInstalls(appRoot, installDir);
            return Path.Combine(installDir, AppExecutableName);
        }
        catch (Exception ex)
        {
            Debug.WriteLine(ex);

            try
            {
                return FindFallbackAppPath(launcherRoot, appRoot, currentTagPath, "Update failed. Launching installed version.", statusWindow);
            }
            catch (InvalidOperationException)
            {
                // Report the update failure rather than the missing fallback.
            }

            throw;
        }
    }

    private static string FindFallbackAppPath(
        string launcherRoot,
        string appRoot,
        string currentTagPath,
        string message,
        LauncherStatusWindow statusWindow)
    {
        string? fallbackDir = null;

        var currentTag = TryReadText(currentTagPath);
        if (!string.IsNullOrWhiteSpace(currentTag))
        {
            var currentDir = Path.Combine(appRoot, SanitizePathSegment(currentTag));
            if (IsInstalled(currentDir))
            {
                fallbackDir = currentDir;
            }
        }

        fallbackDir ??= FindNewestInstall(appRoot, InstalledMarkerFileName)
            ?? FindNewestInstall(Path.Combine(launcherRoot, LegacyInstallDirectoryName), LegacyInstalledMarkerFileName);

        if (fallbackDir is null)
        {
            throw new InvalidOperationException("No runnable local version is available.");
        }

        statusWindow.SetStatus(message);
        return Path.Combine(fallbackDir, AppExecutableName);
    }

    private static string? FindNewestInstall(string root, string markerFileName)
    {
        if (!Directory.Exists(root))
        {
            return null;
        }

        return Directory
            .GetDirectories(root)
            .Where(dir => File.Exists(Path.Combine(dir, markerFileName)) && File.Exists(Path.Combine(dir, AppExecutableName)))
            .OrderByDescending(dir => File.GetLastWriteTimeUtc(Path.Combine(dir, markerFileName)))
            .FirstOrDefault();
    }

    private static bool IsInstalled(string installDir)
    {
        return File.Exists(Path.Combine(installDir, InstalledMarkerFileName)) &&
               File.Exists(Path.Combine(installDir, AppExecutableName));
    }

    private static async Task DownloadAndInstallAsync(
        GitHubReleaseAsset releaseAsset,
        string launcherPath,
        string appRoot,
        string installDir,
        LauncherStatusWindow statusWindow)
    {
        var suffix = Guid.NewGuid().ToString("N");
        var archivePath = Path.Combine(appRoot, ".download-" + suffix + ".zip");
        var stagingDir = Path.Combine(appRoot, ".staging-" + suffix);

        try
        {
            statusWindow.SetStatus("Downloading update...", 0);
            await DownloadFileAsync(releaseAsset.DownloadUrl, archivePath, statusWindow).ConfigureAwait(false);

            statusWindow.SetStatus("Installing update...");
            await Task.Run(() => ZipFile.ExtractToDirectory(archivePath, stagingDir, overwriteFiles: true))
                .ConfigureAwait(false);

            statusWindow.SetStatus("Verifying update...");
            VerifyAppSignature(Path.Combine(stagingDir, AppExecutableName), launcherPath);

            File.WriteAllText(Path.Combine(stagingDir, InstalledMarkerFileName), releaseAsset.Tag);

            if (IsInstalled(installDir))
            {
                return;
            }

            if (Directory.Exists(installDir))
            {
                DeleteInstall(installDir);
            }

            Directory.Move(stagingDir, installDir);
        }
        catch when (IsInstalled(installDir))
        {
            // Another launcher instance finished installing the same release.
        }
        finally
        {
            TryDeleteFile(archivePath);
            if (Directory.Exists(stagingDir))
            {
                TryDeleteDirectory(stagingDir);
            }
        }
    }

    /// <summary>
    /// Requires the downloaded app to carry a valid Authenticode signature from
    /// the same publisher that signed this launcher. Unsigned development builds
    /// of the launcher skip the check.
    /// </summary>
    private static void VerifyAppSignature(string appPath, string launcherPath)
    {
        if (!File.Exists(appPath))
        {
            throw new InvalidDataException("Downloaded update does not contain Valour.exe.");
        }

        var expectedPublisher = Authenticode.TryGetSignerSubject(launcherPath);
        if (expectedPublisher is null)
        {
            return;
        }

        if (!Authenticode.IsSignatureValid(appPath))
        {
            throw new InvalidDataException("Downloaded update is not validly signed.");
        }

        var actualPublisher = Authenticode.TryGetSignerSubject(appPath);
        if (!string.Equals(actualPublisher, expectedPublisher, StringComparison.Ordinal))
        {
            throw new InvalidDataException($"Downloaded update is signed by an unexpected publisher: {actualPublisher}");
        }
    }

    private static async Task DownloadFileAsync(string url, string destinationPath, LauncherStatusWindow statusWindow)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        using var response = await GitHubClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        var totalBytes = response.Content.Headers.ContentLength;

        await using var downloadStream = await response.Content.ReadAsStreamAsync().ConfigureAwait(false);
        await using var destinationStream = File.Create(destinationPath);

        var buffer = new byte[1024 * 128];
        long downloaded = 0;
        var lastPercent = -1;

        while (true)
        {
            var read = await downloadStream.ReadAsync(buffer.AsMemory(0, buffer.Length)).ConfigureAwait(false);
            if (read <= 0)
            {
                break;
            }

            await destinationStream.WriteAsync(buffer.AsMemory(0, read)).ConfigureAwait(false);
            downloaded += read;

            if (totalBytes is > 0)
            {
                var percent = Math.Clamp((int)(downloaded * 100 / totalBytes.Value), 0, 100);
                if (percent != lastPercent)
                {
                    statusWindow.SetStatus("Downloading update...", percent);
                    lastPercent = percent;
                }
            }
        }
    }

    /// <summary>
    /// Copies this launcher to a fixed path that the app uses to restart for
    /// updates. When running from a legacy "Valour-full.exe", only the signed
    /// launcher portion is copied, which leaves an intact signature.
    /// </summary>
    private static void TryInstallLauncherCopy(string launcherPath, string installedLauncherPath)
    {
        try
        {
            if (string.Equals(
                    Path.GetFullPath(launcherPath),
                    Path.GetFullPath(installedLauncherPath),
                    StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            using var source = File.OpenRead(launcherPath);
            var launcherLength = GetLauncherImageLength(source);

            if (File.Exists(installedLauncherPath) &&
                new FileInfo(installedLauncherPath).Length == launcherLength &&
                FileVersionInfo.GetVersionInfo(installedLauncherPath).ProductVersion ==
                FileVersionInfo.GetVersionInfo(launcherPath).ProductVersion)
            {
                return;
            }

            var tempPath = installedLauncherPath + ".tmp-" + Guid.NewGuid().ToString("N");
            try
            {
                source.Seek(0, SeekOrigin.Begin);
                using (var destination = File.Create(tempPath))
                {
                    CopyBytes(source, destination, launcherLength);
                }

                File.Move(tempPath, installedLauncherPath, overwrite: true);
            }
            finally
            {
                TryDeleteFile(tempPath);
            }
        }
        catch (Exception ex)
        {
            // The app falls back to other update paths when no launcher copy exists.
            Debug.WriteLine(ex);
        }
    }

    /// <summary>
    /// Returns the length of the launcher executable, excluding any legacy app
    /// payload that was appended to it.
    /// </summary>
    private static long GetLauncherImageLength(Stream stream)
    {
        var signedLength = Authenticode.GetSignedImageLength(stream);
        if (signedLength is not null)
        {
            return signedLength.Value;
        }

        var trailerLength = sizeof(long) + LegacyPayloadMarker.Length;
        if (stream.Length <= trailerLength)
        {
            return stream.Length;
        }

        var trailer = new byte[trailerLength];
        stream.Seek(-trailerLength, SeekOrigin.End);
        stream.ReadExactly(trailer);
        if (!trailer.AsSpan(sizeof(long)).SequenceEqual(LegacyPayloadMarker))
        {
            return stream.Length;
        }

        var payloadLength = BitConverter.ToInt64(trailer, 0);
        if (payloadLength <= 0 || payloadLength > stream.Length - trailerLength)
        {
            return stream.Length;
        }

        return stream.Length - trailerLength - payloadLength;
    }

    private static void CopyBytes(Stream source, Stream destination, long count)
    {
        var buffer = new byte[1024 * 1024];
        var remaining = count;
        while (remaining > 0)
        {
            var read = source.Read(buffer, 0, (int)Math.Min(buffer.Length, remaining));
            if (read <= 0)
            {
                throw new EndOfStreamException("Unexpected end of launcher data.");
            }

            destination.Write(buffer, 0, read);
            remaining -= read;
        }
    }

    private static string GetFailureMessage(Exception ex)
    {
        if (ex is InvalidDataException)
        {
            return "Downloaded update is invalid.";
        }

        if (ex is InvalidOperationException op && !string.IsNullOrWhiteSpace(op.Message))
        {
            return op.Message;
        }

        return "Failed to start Valour.";
    }

    private static async Task<GitHubReleaseAsset?> FetchLatestReleaseAssetAsync()
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, LatestReleaseApiUrl);
        using var response = await GitHubClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            Debug.WriteLine($"GitHub latest release request returned {(int)response.StatusCode}.");
            return null;
        }

        await using var contentStream = await response.Content.ReadAsStreamAsync().ConfigureAwait(false);
        using var document = await JsonDocument.ParseAsync(contentStream).ConfigureAwait(false);
        var root = document.RootElement;

        if (!root.TryGetProperty("tag_name", out var tagElement))
        {
            return null;
        }

        var tag = tagElement.GetString();
        if (string.IsNullOrWhiteSpace(tag))
        {
            return null;
        }

        if (!root.TryGetProperty("assets", out var assetsElement) || assetsElement.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        foreach (var asset in assetsElement.EnumerateArray())
        {
            if (!asset.TryGetProperty("name", out var nameElement) ||
                !string.Equals(nameElement.GetString(), AppArchiveAssetName, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (!asset.TryGetProperty("browser_download_url", out var urlElement))
            {
                continue;
            }

            var downloadUrl = urlElement.GetString();
            if (string.IsNullOrWhiteSpace(downloadUrl))
            {
                continue;
            }

            return new GitHubReleaseAsset(tag, downloadUrl);
        }

        return null;
    }

    private static HttpClient CreateGitHubClient()
    {
        // Responses are read with ResponseHeadersRead, so the timeout bounds
        // reaching GitHub but not the length of a download.
        var client = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(12)
        };

        client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("ValourLauncher", "1.0"));
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        return client;
    }

    private static void CleanupOldInstalls(string appRoot, string currentInstallDir)
    {
        foreach (var dir in Directory.GetDirectories(appRoot))
        {
            if (string.Equals(dir, currentInstallDir, StringComparison.OrdinalIgnoreCase) ||
                Path.GetFileName(dir).StartsWith(".staging-", StringComparison.Ordinal))
            {
                continue;
            }

            try
            {
                DeleteInstall(dir);
            }
            catch
            {
                // Ignore cleanup failures (usually locked files from an exiting app).
            }
        }
    }

    private static void DeleteInstall(string installDir)
    {
        // Remove the marker first so a partially deleted install is never
        // chosen as a fallback.
        var markerPath = Path.Combine(installDir, InstalledMarkerFileName);
        if (File.Exists(markerPath))
        {
            File.Delete(markerPath);
        }

        Directory.Delete(installDir, recursive: true);
    }

    private static string SanitizePathSegment(string value)
    {
        var invalidChars = Path.GetInvalidFileNameChars();
        var builder = new StringBuilder(value.Length);

        foreach (var c in value)
        {
            builder.Append(Array.IndexOf(invalidChars, c) >= 0 ? '_' : c);
        }

        return builder.Length == 0 ? "unknown" : builder.ToString();
    }

    private static string? TryReadText(string path)
    {
        try
        {
            if (!File.Exists(path))
            {
                return null;
            }

            var value = File.ReadAllText(path).Trim();
            return string.IsNullOrWhiteSpace(value) ? null : value;
        }
        catch
        {
            return null;
        }
    }

    private static void TryWriteText(string path, string value)
    {
        try
        {
            File.WriteAllText(path, value.Trim());
        }
        catch
        {
            // Ignore state write failures.
        }
    }

    private static void TryDeleteFile(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch
        {
            // Ignore temporary cleanup failures.
        }
    }

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            Directory.Delete(path, recursive: true);
        }
        catch
        {
            // Ignore temporary cleanup failures.
        }
    }

    private sealed record GitHubReleaseAsset(string Tag, string DownloadUrl);
}
