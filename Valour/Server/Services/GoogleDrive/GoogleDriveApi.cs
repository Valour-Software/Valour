using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Valour.Config.Configs;

namespace Valour.Server.Services.GoogleDrive;

/// <summary>The Google Drive v3 REST API over HTTP.</summary>
public class GoogleDriveApi : IGoogleDriveApi
{
    public const string HttpClientName = "GoogleDrive";

    private static string TokenUrl => GoogleEndpoints.Token;
    private static string RevokeUrl => GoogleEndpoints.Revoke;
    private static string FilesUrl => GoogleEndpoints.DriveFiles;
    private static string UploadUrl => GoogleEndpoints.DriveUpload;
    private const string FolderMimeType = "application/vnd.google-apps.folder";

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<GoogleDriveApi> _logger;

    public GoogleDriveApi(IHttpClientFactory httpClientFactory, ILogger<GoogleDriveApi> logger)
    {
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    private HttpClient Http => _httpClientFactory.CreateClient(HttpClientName);

    public async Task<DriveAccessToken> RefreshAccessTokenAsync(string refreshToken, CancellationToken cancellationToken = default)
    {
        var google = ExternalAuthConfig.Current?.Google;
        if (google?.IsConfigured != true)
            throw new DriveApiException(DriveErrorKind.Other, "Google is not configured.");

        using var response = await SendAsync(() => new HttpRequestMessage(HttpMethod.Post, TokenUrl)
        {
            Content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["grant_type"] = "refresh_token",
                ["refresh_token"] = refreshToken,
                ["client_id"] = google.ClientId!,
                ["client_secret"] = google.ClientSecret!,
            })
        }, cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken);

            // Google answers a revoked or expired grant with invalid_grant.
            if (response.StatusCode is HttpStatusCode.BadRequest or HttpStatusCode.Unauthorized &&
                body.Contains("invalid_grant", StringComparison.Ordinal))
                throw new DriveApiException(DriveErrorKind.AccessRevoked, "Google no longer allows Valour to use this Drive.");

            throw ToException(response.StatusCode, body, "refreshing access");
        }

        var token = await response.Content.ReadFromJsonAsync<TokenResponse>(cancellationToken);
        if (string.IsNullOrEmpty(token?.AccessToken))
            throw new DriveApiException(DriveErrorKind.Other, "Google returned no access token.");

        // Renew a little early so a token never expires partway through an upload.
        return new DriveAccessToken(token.AccessToken, DateTime.UtcNow.AddSeconds(Math.Max(60, token.ExpiresIn - 120)));
    }

    public async Task<string> CreateFolderAsync(string accessToken, string name, string parentId, CancellationToken cancellationToken = default)
    {
        var metadata = new Dictionary<string, object>
        {
            ["name"] = name,
            ["mimeType"] = FolderMimeType,
            ["appProperties"] = new Dictionary<string, string> { ["valour"] = "1" },
        };
        if (parentId is not null)
            metadata["parents"] = new[] { parentId };

        using var response = await SendAsync(() => Authorized(HttpMethod.Post, $"{FilesUrl}?fields=id", accessToken,
            JsonContent(metadata)), cancellationToken);

        var created = await ReadAsync<FileResponse>(response, "creating a folder", cancellationToken);
        return created.Id;
    }

    public async Task<bool> FolderExistsAsync(string accessToken, string folderId, CancellationToken cancellationToken = default)
    {
        using var response = await SendAsync(() => Authorized(HttpMethod.Get,
            $"{FilesUrl}/{Uri.EscapeDataString(folderId)}?fields=id,trashed", accessToken), cancellationToken);

        if (response.StatusCode == HttpStatusCode.NotFound)
            return false;

        var file = await ReadAsync<FileResponse>(response, "checking a folder", cancellationToken);
        return file.Trashed != true;
    }

    public async Task RenameAsync(string accessToken, string fileId, string name, CancellationToken cancellationToken = default)
    {
        using var response = await SendAsync(() => Authorized(new HttpMethod("PATCH"),
            $"{FilesUrl}/{Uri.EscapeDataString(fileId)}?fields=id", accessToken,
            JsonContent(new Dictionary<string, object> { ["name"] = name })), cancellationToken);

        await ReadAsync<FileResponse>(response, "renaming a folder", cancellationToken);
    }

    public async Task<string> UploadFileAsync(
        string accessToken,
        string parentId,
        string name,
        string mimeType,
        Stream content,
        long length,
        IReadOnlyDictionary<string, string> properties,
        CancellationToken cancellationToken = default)
    {
        var metadata = new Dictionary<string, object>
        {
            ["name"] = name,
            ["mimeType"] = mimeType,
            ["parents"] = new[] { parentId },
        };
        if (properties is { Count: > 0 })
            metadata["appProperties"] = properties;

        // A resumable upload starts with the details, then sends the bytes in
        // one request to the address Google returns.
        using var start = await SendAsync(() =>
        {
            var request = Authorized(HttpMethod.Post, $"{UploadUrl}?uploadType=resumable&fields=id", accessToken, JsonContent(metadata));
            request.Headers.TryAddWithoutValidation("X-Upload-Content-Type", mimeType);
            request.Headers.TryAddWithoutValidation("X-Upload-Content-Length", length.ToString());
            return request;
        }, cancellationToken);

        if (!start.IsSuccessStatusCode)
            throw ToException(start.StatusCode, await start.Content.ReadAsStringAsync(cancellationToken), "starting an upload");

        var location = start.Headers.Location;
        if (location is null)
            throw new DriveApiException(DriveErrorKind.Other, "Google did not return an upload address.");

        using var upload = new HttpRequestMessage(HttpMethod.Put, location)
        {
            Content = new StreamContent(content)
        };
        upload.Content.Headers.ContentLength = length;
        upload.Content.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");

        HttpResponseMessage response;
        try
        {
            response = await Http.SendAsync(upload, cancellationToken);
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException && !cancellationToken.IsCancellationRequested)
        {
            throw new DriveApiException(DriveErrorKind.Transient, "The upload to Google Drive was interrupted.", e);
        }

        using (response)
        {
            var uploaded = await ReadAsync<FileResponse>(response, "uploading a file", cancellationToken);
            return uploaded.Id;
        }
    }

    public async Task<DriveDownload> OpenDownloadAsync(string accessToken, string fileId, CancellationToken cancellationToken = default)
    {
        HttpResponseMessage response;
        try
        {
            using var request = Authorized(HttpMethod.Get, $"{FilesUrl}/{Uri.EscapeDataString(fileId)}?alt=media", accessToken);
            response = await Http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException && !cancellationToken.IsCancellationRequested)
        {
            throw new DriveApiException(DriveErrorKind.Transient, "Could not reach Google Drive.", e);
        }

        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            var status = response.StatusCode;
            response.Dispose();
            throw ToException(status, body, "downloading a file");
        }

        return new DriveDownload(await response.Content.ReadAsStreamAsync(cancellationToken), response.Content.Headers.ContentLength, response);
    }

    public async Task RevokeAsync(string token)
    {
        try
        {
            using var response = await Http.PostAsync(RevokeUrl, new FormUrlEncodedContent(new Dictionary<string, string> { ["token"] = token }));
            if (!response.IsSuccessStatusCode)
                _logger.LogInformation("Google did not revoke a Drive grant ({Status}). It may already be revoked.", (int)response.StatusCode);
        }
        catch (Exception e)
        {
            _logger.LogInformation(e, "Could not tell Google to revoke a Drive grant.");
        }
    }

    // Helpers //

    private static HttpRequestMessage Authorized(HttpMethod method, string url, string accessToken, HttpContent content = null)
    {
        var request = new HttpRequestMessage(method, url) { Content = content };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        return request;
    }

    private static StringContent JsonContent(object value) =>
        new(JsonSerializer.Serialize(value), Encoding.UTF8, "application/json");

    /// <summary>Sends a request that is safe to repeat, retrying once on a dropped connection.</summary>
    private async Task<HttpResponseMessage> SendAsync(Func<HttpRequestMessage> build, CancellationToken cancellationToken)
    {
        try
        {
            using var request = build();
            return await Http.SendAsync(request, cancellationToken);
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException && !cancellationToken.IsCancellationRequested)
        {
            throw new DriveApiException(DriveErrorKind.Transient, "Could not reach Google Drive.", e);
        }
    }

    private static async Task<T> ReadAsync<T>(HttpResponseMessage response, string action, CancellationToken cancellationToken)
    {
        if (!response.IsSuccessStatusCode)
            throw ToException(response.StatusCode, await response.Content.ReadAsStringAsync(cancellationToken), action);

        return await response.Content.ReadFromJsonAsync<T>(cancellationToken)
               ?? throw new DriveApiException(DriveErrorKind.Other, $"Google returned nothing while {action}.");
    }

    /// <summary>Sorts a failed response into something the sync logic can act on.</summary>
    internal static DriveApiException ToException(HttpStatusCode status, string body, string action)
    {
        var message = $"Google Drive refused {action} ({(int)status}).";

        if (status == HttpStatusCode.Unauthorized)
            return new DriveApiException(DriveErrorKind.AccessRevoked, message);

        if (status == HttpStatusCode.NotFound)
            return new DriveApiException(DriveErrorKind.NotFound, message);

        if (status == HttpStatusCode.TooManyRequests || (int)status >= 500)
            return new DriveApiException(DriveErrorKind.Transient, message);

        if (status == HttpStatusCode.Forbidden)
        {
            // Google puts the reason in the error body.
            if (body.Contains("storageQuotaExceeded", StringComparison.Ordinal))
                return new DriveApiException(DriveErrorKind.DriveFull, "Your Google Drive is full.");

            if (body.Contains("RateLimitExceeded", StringComparison.OrdinalIgnoreCase))
                return new DriveApiException(DriveErrorKind.Transient, message);

            // Access can be taken away without the grant being revoked, such as
            // when the Drive API is switched off for the app's project.
            if (body.Contains("insufficientPermissions", StringComparison.Ordinal) ||
                body.Contains("insufficient authentication scopes", StringComparison.OrdinalIgnoreCase))
                return new DriveApiException(DriveErrorKind.AccessRevoked, message);
        }

        return new DriveApiException(DriveErrorKind.Other, message);
    }

    private sealed class TokenResponse
    {
        [JsonPropertyName("access_token")] public string AccessToken { get; set; }
        [JsonPropertyName("expires_in")] public int ExpiresIn { get; set; }
    }

    private sealed class FileResponse
    {
        [JsonPropertyName("id")] public string Id { get; set; }
        [JsonPropertyName("trashed")] public bool? Trashed { get; set; }
    }
}
