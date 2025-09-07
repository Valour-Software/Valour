using Valour.Sdk.Client;
using Valour.Sdk.Requests;
using Valour.Sdk.Models;
using Valour.Shared.Authorization;
using Valour.Shared.Models;
using System.Net.Http.Json;
using Valour.Shared;
using Valour.Sdk.ModelLogic;

namespace Valour.Sdk.Services;

/// <summary>
/// Service for managing OAuth applications and handling OAuth flows
/// </summary>
public class OauthService : ServiceBase
{
    private readonly LogOptions _logOptions = new(
        "OauthService",
        "#3381a3",
        "#a3333e",
        "#a39433"
    );
    
    private readonly ValourClient _client;

    public OauthService(ValourClient client)
    {
        _client = client;
        SetupLogging(_client.Logger, _logOptions);
    }
    
    #region App Management

    /// <summary>
    /// Fetches all OAuth apps owned by the current user
    /// </summary>
    public async Task<List<OauthApp>> FetchMyOauthAppAsync(){
        var apps = (await _client.PrimaryNode.GetJsonAsync<List<OauthApp>>("api/users/apps")).Data;
        apps.SyncAll(_client);
        return apps;
    }

    /// <summary>
    /// Fetches a specific OAuth app by ID (must be the owner)
    /// </summary>
    public async Task<OauthApp> FetchAppAsync(long id) {
        var app =(await _client.PrimaryNode.GetJsonAsync<OauthApp>($"api/oauth/app/{id}")).Data;
        app = app.Sync(_client);
        return app;
    }

    /// <summary>
    /// Fetches public data for an OAuth app (no authentication required)
    /// </summary>
    public async Task<PublicOauthAppData> FetchAppPublicDataAsync(long id) =>
        (await _client.PrimaryNode.GetJsonAsync<PublicOauthAppData>($"api/oauth/app/public/{id}")).Data;

    /// <summary>
    /// Creates a new OAuth application
    /// </summary>
    public async Task<TaskResult<OauthApp>> CreateAppAsync(CreateOauthAppRequest request)
    {
        try
        {
            // Create the OAuth app model
            var app = new OauthApp(_client)
            {
                Name = request.Name,
                RedirectUrl = request.RedirectUrl,
                ImageUrl = "../_content/Valour.Client/media/logo/logo-512.png", // Default image
                OwnerId = _client.Me.Id,
                Uses = 0
            };

            var response = await _client.PrimaryNode.PostAsyncWithResponse<long>("api/oauth/app", app);
            
            if (!response.Success)
                return new TaskResult<OauthApp>(false, response.Message);

            // Fetch the created app to get the complete data including secret
            var createdApp = await FetchAppAsync(response.Data);
            return new TaskResult<OauthApp>(true, "OAuth app created successfully", createdApp);
        }
        catch (Exception ex)
        {
            LogError($"Failed to create OAuth app: {ex.Message}");
            return new TaskResult<OauthApp>(false, $"Failed to create OAuth app: {ex.Message}");
        }
    }

    /// <summary>
    /// Updates an existing OAuth application
    /// </summary>
    public async Task<TaskResult<OauthApp>> UpdateAppAsync(long appId, UpdateOauthAppRequest request)
    {
        try
        {
            var app = await FetchAppAsync(appId);
            if (app == null)
                return new TaskResult<OauthApp>(false, "OAuth app not found");

            // Update only the provided fields
            if (request.Name != null)
                app.Name = request.Name;
            if (request.RedirectUrl != null)
                app.RedirectUrl = request.RedirectUrl;
            // Note: Image updates should be done via the upload endpoint, not through this method

            var result = await app.UpdateAsync();
            if (!result.Success)
                return new TaskResult<OauthApp>(false, result.Message);

            return new TaskResult<OauthApp>(true, "OAuth app updated successfully", app);
        }
        catch (Exception ex)
        {
            LogError($"Failed to update OAuth app: {ex.Message}");
            return new TaskResult<OauthApp>(false, $"Failed to update OAuth app: {ex.Message}");
        }
    }

    /// <summary>
    /// Deletes an OAuth application
    /// </summary>
    public async Task<TaskResult> DeleteAppAsync(long appId)
    {
        try
        {
            var app = await FetchAppAsync(appId);
            if (app == null)
                return new TaskResult(false, "OAuth app not found");

            var result = await app.DeleteAsync();
            return new TaskResult(result.Success, result.Message);
        }
        catch (Exception ex)
        {
            LogError($"Failed to delete OAuth app: {ex.Message}");
            return new TaskResult(false, $"Failed to delete OAuth app: {ex.Message}");
        }
    }

    #endregion

    #region Image Management

    /// <summary>
    /// Uploads an image for an OAuth application
    /// </summary>
    public async Task<TaskResult<string>> UploadAppImageAsync(long appId, Stream imageStream, string fileName)
    {
        try
        {
            // Create form data for the upload
            var formData = new MultipartFormDataContent();
            var fileContent = new StreamContent(imageStream);
            formData.Add(fileContent, "file", fileName);

            // Upload to the CDN endpoint
            var response = await _client.Http.PostAsync($"upload/app/{appId}", formData);
            
            if (response.IsSuccessStatusCode)
            {
                var imageUrl = await response.Content.ReadAsStringAsync();
                return new TaskResult<string>(true, "Image uploaded successfully", imageUrl);
            }
            else
            {
                var errorMessage = await response.Content.ReadAsStringAsync();
                return new TaskResult<string>(false, $"Failed to upload image: {errorMessage}");
            }
        }
        catch (Exception ex)
        {
            LogError($"Failed to upload OAuth app image: {ex.Message}");
            return new TaskResult<string>(false, $"Failed to upload image: {ex.Message}");
        }
    }

    #endregion

    #region OAuth Flow

    /// <summary>
    /// Initiates OAuth authorization flow
    /// </summary>
    public async Task<TaskResult<OauthAuthorizationResponse>> AuthorizeAsync(OauthAuthorizationRequest request)
    {
        try
        {
            var model = new AuthorizeModel
            {
                ClientId = request.ClientId,
                RedirectUri = request.RedirectUri,
                UserId = _client.Me.Id,
                ResponseType = request.ResponseType,
                Scope = request.Scope,
                State = request.State
            };

            var response = await _client.PrimaryNode.PostAsyncWithResponse<string>("api/oauth/authorize", model);
            
            if (!response.Success)
                return new TaskResult<OauthAuthorizationResponse>(false, response.Message);

            // Parse the redirect URL to extract parameters
            var redirectUrl = response.Data;
            var uri = new Uri(redirectUrl);
            var query = System.Web.HttpUtility.ParseQueryString(uri.Query);

            var authResponse = new OauthAuthorizationResponse
            {
                Code = query["code"],
                State = query["state"],
                Node = query["node"],
                RedirectUrl = redirectUrl
            };

            return new TaskResult<OauthAuthorizationResponse>(true, "Authorization successful", authResponse);
        }
        catch (Exception ex)
        {
            LogError($"Failed to authorize OAuth: {ex.Message}");
            return new TaskResult<OauthAuthorizationResponse>(false, $"Failed to authorize OAuth: {ex.Message}");
        }
    }

    /// <summary>
    /// Exchanges authorization code for access token
    /// </summary>
    public async Task<TaskResult<AuthToken>> ExchangeCodeForTokenAsync(OauthTokenRequest request)
    {
        try
        {
            // Build query parameters
            var queryParams = new List<string>
            {
                $"client_id={request.ClientId}",
                $"client_secret={Uri.EscapeDataString(request.ClientSecret)}",
                $"grant_type={Uri.EscapeDataString(request.GrantType)}",
                $"code={Uri.EscapeDataString(request.Code)}",
                $"redirect_uri={Uri.EscapeDataString(request.RedirectUri)}"
            };

            if (!string.IsNullOrEmpty(request.State))
                queryParams.Add($"state={Uri.EscapeDataString(request.State)}");

            var queryString = string.Join("&", queryParams);
            var response = await _client.PrimaryNode.GetJsonAsync<AuthToken>($"api/oauth/token?{queryString}");
            
            if (!response.Success)
                return new TaskResult<AuthToken>(false, response.Message);

            return new TaskResult<AuthToken>(true, "Token exchange successful", response.Data);
        }
        catch (Exception ex)
        {
            LogError($"Failed to exchange code for token: {ex.Message}");
            return new TaskResult<AuthToken>(false, $"Failed to exchange code for token: {ex.Message}");
        }
    }

    /// <summary>
    /// Generates a random state parameter for OAuth security
    /// </summary>
    public string GenerateState()
    {
        return Guid.NewGuid().ToString("N");
    }

    /// <summary>
    /// Builds the authorization URL for OAuth flow
    /// </summary>
    public string BuildAuthorizationUrl(long clientId, string redirectUri, long scope = 0, string? state = null)
    {
        var queryParams = new List<string>
        {
            $"response_type=code",
            $"client_id={clientId}",
            $"redirect_uri={Uri.EscapeDataString(redirectUri)}",
            $"scope={scope}"
        };

        if (!string.IsNullOrEmpty(state))
            queryParams.Add($"state={Uri.EscapeDataString(state)}");

        var queryString = string.Join("&", queryParams);
        return $"{_client.PrimaryNode.HttpClient.BaseAddress}/authorize?{queryString}";
    }

    #endregion

    #region Utility Methods

    /// <summary>
    /// Validates OAuth app name
    /// </summary>
    public static TaskResult ValidateAppName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return new TaskResult(false, "App name cannot be empty");

        if (name.Length > 32)
            return new TaskResult(false, "App name must be 32 characters or less");

        return new TaskResult(true, "App name is valid");
    }

    /// <summary>
    /// Validates redirect URL
    /// </summary>
    public static TaskResult ValidateRedirectUrl(string url)
    {
        if (string.IsNullOrWhiteSpace(url))
            return new TaskResult(false, "Redirect URL cannot be empty");

        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
            return new TaskResult(false, "Redirect URL must be a valid absolute URL");

        if (uri.Scheme != "http" && uri.Scheme != "https")
            return new TaskResult(false, "Redirect URL must use HTTP or HTTPS scheme");

        return new TaskResult(true, "Redirect URL is valid");
    }

    #endregion
}