using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json.Serialization;

namespace Valour.Client.Device;

public class AppUpdateInfo
{
    public required string LatestVersion { get; init; }
    public required string ReleasePageUrl { get; init; }
    public string? ApkDownloadUrl { get; init; }
}

/// <summary>
/// Checks GitHub releases for a newer build of the native shell.
/// Releases are tagged "v{ApplicationDisplayVersion}" by the Android workflow.
/// </summary>
public static class AppUpdateChecker
{
    public const string LatestReleaseApiUrl = "https://api.github.com/repos/Valour-Software/Valour/releases/latest";
    public const string ReleasesPageUrl = "https://github.com/Valour-Software/Valour/releases/latest";

    /// <summary>
    /// Returns update info if a release newer than <paramref name="currentVersion"/>
    /// exists, otherwise null. Never throws.
    /// </summary>
    public static async Task<AppUpdateInfo?> CheckForUpdateAsync(HttpClient http, string currentVersion)
    {
        if (!Version.TryParse(currentVersion, out var current))
            return null;

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, LatestReleaseApiUrl);
            request.Headers.UserAgent.ParseAdd("Valour-Client");
            request.Headers.Accept.ParseAdd("application/vnd.github+json");

            using var response = await http.SendAsync(request);
            if (!response.IsSuccessStatusCode)
                return null;

            var release = await response.Content.ReadFromJsonAsync<GithubRelease>();
            if (release?.TagName is null)
                return null;

            if (!Version.TryParse(release.TagName.TrimStart('v', 'V'), out var latest))
                return null;

            if (latest <= current)
                return null;

            var apkAsset = release.Assets?.FirstOrDefault(a =>
                a.Name is not null &&
                a.Name.EndsWith(".apk", StringComparison.OrdinalIgnoreCase));

            return new AppUpdateInfo
            {
                LatestVersion = latest.ToString(),
                ReleasePageUrl = release.HtmlUrl ?? ReleasesPageUrl,
                ApkDownloadUrl = apkAsset?.BrowserDownloadUrl,
            };
        }
        catch
        {
            // Update checks must never break startup
            return null;
        }
    }

    private class GithubRelease
    {
        [JsonPropertyName("tag_name")]
        public string? TagName { get; set; }

        [JsonPropertyName("html_url")]
        public string? HtmlUrl { get; set; }

        [JsonPropertyName("assets")]
        public List<GithubAsset>? Assets { get; set; }
    }

    private class GithubAsset
    {
        [JsonPropertyName("name")]
        public string? Name { get; set; }

        [JsonPropertyName("browser_download_url")]
        public string? BrowserDownloadUrl { get; set; }
    }
}
