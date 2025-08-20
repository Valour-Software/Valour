using Valour.Sdk.Client;
using Valour.Sdk.Requests;
using Valour.Sdk.Models;
using Valour.Shared.Models;
using Valour.Shared;
using Valour.Shared.Authorization;

namespace Valour.Sdk.Services;

/// <summary>
/// High-level helper class for OAuth operations
/// Provides easy-to-use methods for common OAuth workflows
/// </summary>
public class OauthHelper
{
    private readonly ValourClient _client;
    private readonly OauthService _oauthService;

    public OauthHelper(ValourClient client)
    {
        _client = client;
        _oauthService = client.OauthService;
    }

    #region App Management Helpers

    /// <summary>
    /// Creates a new OAuth app with default settings
    /// </summary>
    public async Task<TaskResult<OauthApp>> CreateSimpleAppAsync(string name, string redirectUrl)
    {
        var request = new CreateOauthAppRequest
        {
            Name = name,
            RedirectUrl = redirectUrl
        };

        return await _oauthService.CreateAppAsync(request);
    }

    /// <summary>
    /// Creates a new OAuth app with custom image
    /// </summary>
    public async Task<TaskResult<OauthApp>> CreateAppWithImageAsync(string name, string redirectUrl, Stream imageStream, string fileName)
    {
        // First create the app
        var createResult = await CreateSimpleAppAsync(name, redirectUrl);
        if (!createResult.Success)
            return createResult;

        // Then upload the image
        var uploadResult = await _oauthService.UploadAppImageAsync(createResult.Data.Id, imageStream, fileName);
        if (!uploadResult.Success)
        {
            // If image upload fails, we still have the app created
            return new TaskResult<OauthApp>(true, $"App created but image upload failed: {uploadResult.Message}", createResult.Data);
        }

        return createResult;
    }

    /// <summary>
    /// Updates only the redirect URL of an OAuth app
    /// </summary>
    public async Task<TaskResult<OauthApp>> UpdateRedirectUrlAsync(long appId, string newRedirectUrl)
    {
        var request = new UpdateOauthAppRequest
        {
            RedirectUrl = newRedirectUrl
        };

        return await _oauthService.UpdateAppAsync(appId, request);
    }

    /// <summary>
    /// Updates only the name of an OAuth app
    /// </summary>
    public async Task<TaskResult<OauthApp>> UpdateAppNameAsync(long appId, string newName)
    {
        var request = new UpdateOauthAppRequest
        {
            Name = newName
        };

        return await _oauthService.UpdateAppAsync(appId, request);
    }

    /// <summary>
    /// Updates the image of an OAuth app using the secure upload system
    /// </summary>
    public async Task<TaskResult<string>> UpdateAppImageAsync(long appId, Stream imageStream, string fileName)
    {
        return await _oauthService.UploadAppImageAsync(appId, imageStream, fileName);
    }

    #endregion

    #region OAuth Flow Helpers

    /// <summary>
    /// Initiates OAuth authorization with automatic state generation
    /// </summary>
    public async Task<TaskResult<OauthAuthorizationResponse>> AuthorizeWithAutoStateAsync(long clientId, string redirectUri, long scope = 0)
    {
        var state = _oauthService.GenerateState();
        
        var request = new OauthAuthorizationRequest
        {
            ClientId = clientId,
            RedirectUri = redirectUri,
            ResponseType = "code",
            Scope = scope,
            State = state
        };

        return await _oauthService.AuthorizeAsync(request);
    }

    /// <summary>
    /// Exchanges authorization code for access token with simplified parameters
    /// </summary>
    public async Task<TaskResult<AuthToken>> ExchangeCodeAsync(long clientId, string clientSecret, string code, string redirectUri, string? state = null)
    {
        var request = new OauthTokenRequest
        {
            ClientId = clientId,
            ClientSecret = clientSecret,
            GrantType = "authorization_code",
            Code = code,
            RedirectUri = redirectUri,
            State = state
        };

        return await _oauthService.ExchangeCodeForTokenAsync(request);
    }

    /// <summary>
    /// Complete OAuth flow from start to finish
    /// Returns the access token after successful authorization
    /// </summary>
    public async Task<TaskResult<AuthToken>> CompleteOauthFlowAsync(long clientId, string clientSecret, string redirectUri, long scope = 0)
    {
        try
        {
            // Step 1: Authorize
            var authResult = await AuthorizeWithAutoStateAsync(clientId, redirectUri, scope);
            if (!authResult.Success)
                return new TaskResult<AuthToken>(false, $"Authorization failed: {authResult.Message}");

            // Step 2: Exchange code for token
            var tokenResult = await ExchangeCodeAsync(clientId, clientSecret, authResult.Data.Code, redirectUri, authResult.Data.State);
            if (!tokenResult.Success)
                return new TaskResult<AuthToken>(false, $"Token exchange failed: {tokenResult.Message}");

            return new TaskResult<AuthToken>(true, "OAuth flow completed successfully", tokenResult.Data);
        }
        catch (Exception ex)
        {
            return new TaskResult<AuthToken>(false, $"OAuth flow failed: {ex.Message}");
        }
    }

    #endregion

    #region URL Generation Helpers

    /// <summary>
    /// Generates the authorization URL for redirecting users
    /// </summary>
    public string GetAuthorizationUrl(long clientId, string redirectUri, long scope = 0, string? state = null)
    {
        return _oauthService.BuildAuthorizationUrl(clientId, redirectUri, scope, state);
    }

    /// <summary>
    /// Generates the authorization URL with automatic state generation
    /// </summary>
    public string GetAuthorizationUrlWithAutoState(long clientId, string redirectUri, long scope = 0)
    {
        var state = _oauthService.GenerateState();
        return _oauthService.BuildAuthorizationUrl(clientId, redirectUri, scope, state);
    }

    #endregion

    #region Validation Helpers

    /// <summary>
    /// Validates all OAuth app creation parameters
    /// </summary>
    public TaskResult ValidateAppCreation(string name, string redirectUrl)
    {
        var nameValidation = OauthService.ValidateAppName(name);
        if (!nameValidation.Success)
            return nameValidation;

        var urlValidation = OauthService.ValidateRedirectUrl(redirectUrl);
        if (!urlValidation.Success)
            return urlValidation;

        return new TaskResult(true, "All parameters are valid");
    }

    /// <summary>
    /// Validates OAuth authorization parameters
    /// </summary>
    public TaskResult ValidateAuthorizationParams(long clientId, string redirectUri)
    {
        if (clientId <= 0)
            return new TaskResult(false, "Client ID must be a positive number");

        var urlValidation = OauthService.ValidateRedirectUrl(redirectUri);
        if (!urlValidation.Success)
            return urlValidation;

        return new TaskResult(true, "Authorization parameters are valid");
    }

    #endregion

    #region Utility Methods

    /// <summary>
    /// Gets all OAuth apps owned by the current user
    /// </summary>
    public async Task<List<OauthApp>> GetMyAppsAsync()
    {
        return await _oauthService.FetchMyOauthAppAsync();
    }

    /// <summary>
    /// Gets a specific OAuth app by ID
    /// </summary>
    public async Task<OauthApp?> GetAppAsync(long appId)
    {
        try
        {
            return await _oauthService.FetchAppAsync(appId);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Gets public information about an OAuth app
    /// </summary>
    public async Task<PublicOauthAppData?> GetAppPublicInfoAsync(long appId)
    {
        try
        {
            return await _oauthService.FetchAppPublicDataAsync(appId);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Deletes an OAuth app
    /// </summary>
    public async Task<TaskResult> DeleteAppAsync(long appId)
    {
        return await _oauthService.DeleteAppAsync(appId);
    }

    /// <summary>
    /// Generates a secure random state parameter
    /// </summary>
    public string GenerateSecureState()
    {
        return _oauthService.GenerateState();
    }

    #endregion
}
