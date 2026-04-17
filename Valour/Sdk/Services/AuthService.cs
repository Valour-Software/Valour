using System.Net.Http.Json;
using System.Text.Json;
using Valour.Sdk.Client;
using Valour.Shared;
using Valour.Shared.Authorization;
using Valour.Shared.Models;
using Valour.Shared.Nodes;
using Valour.Shared.Utilities;

namespace Valour.Sdk.Services;

/// <summary>
/// Handles tokens and authentication
/// </summary>
public class AuthService : ServiceBase
{
    /// <summary>
    /// Run when the user logs in
    /// </summary>
    public HybridEvent<User> LoggedIn;
    
    /// <summary>
    /// The token for this client instance
    /// </summary>
    public string Token => _token;

    /// <summary>
    /// The internal token for this client
    /// </summary>
    private string _token;

    private static readonly LogOptions LogOptions = new(
        "AuthService",
        "#0083ab",
        "#ab0055",
        "#ab8900"
    );

    private readonly ValourClient _client;
    
    public AuthService(ValourClient client)
    {
        _client = client;
        SetupLogging(client.Logger, LogOptions);
    }
    
    /// <summary>
    /// Gets the Token for the client
    /// </summary>
    public async Task<AuthResult> FetchToken(string email, string password, string multiFactorCode = null)
    {
        await EnsureAuthorityOriginAsync();

        TokenRequest request = new()
        {
            Email = email,
            Password = password,
            MultiFactorCode = multiFactorCode
        };

        var httpContent = JsonContent.Create(request);
        using var authorityClient = _client.CreateAuthorityHttpClient();
        var response = await authorityClient.PostAsync("api/users/token", httpContent);
        
        
        if (response.IsSuccessStatusCode)
        {
            var result = await response.Content.ReadFromJsonAsync<AuthResult>();

            if (result.Token is not null){
                _token = result.Token.Id;
            }

            return result;
        }

        return new AuthResult()
        {
            Success = false,
            Message = await response.Content.ReadAsStringAsync(),
            Code = (int) response.StatusCode
        };
    }
    
    public void SetToken(string token)
    {
        _token = token;
    }

    public async Task<AuthResult> LoginAsync(string email, string password, string multiFactorCode = null)
    {
        var tokenResult = await FetchToken(email, password, multiFactorCode);
        if (!tokenResult.Success)
        {
            return tokenResult;
        }

        var loginResult = await LoginAsync();
        if (!loginResult.Success)
        {
            return new AuthResult()
            {
                Success = false,
                Message = loginResult.Message,
            };
        }

        return new AuthResult()
        {
            Success = true,
            Message = "Success"
        };
    }
    
    public async Task<TaskResult> LoginAsync()
    {
        if (_client.PrimaryNode is null)
        {
            var nodeResult = await _client.NodeService.SetupPrimaryNodeAsync();
            if (!nodeResult.Success)
                return nodeResult;
        }
        else
        {
            // Update the home node token if it's already been set.
            await _client.HomeNode.UpdateTokenAsync();
            await _client.NodeService.EnsureAuthorityNodeAsync();
            if (_client.AuthorityNode is not null && _client.AuthorityNode != _client.HomeNode)
                await _client.AuthorityNode.UpdateTokenAsync();
        }

        var response = await _client.AccountNode!.GetJsonAsync<User>($"api/users/me");

        if (!response.Success)
            return response.WithoutData();

        _client.Me = response.Data.Sync(_client);
        
        LoggedIn?.Invoke(_client.Me);

        return new TaskResult(true, "Success");
    }

    public async Task<TaskResult> RegisterAsync(RegisterUserRequest request)
    {
        await EnsureAuthorityOriginAsync();

        var content = JsonContent.Create(request);
        using var authorityClient = _client.CreateAuthorityHttpClient();
        var result = await authorityClient.PostAsync("api/users/register", content);

        if (result.IsSuccessStatusCode)
        {
            return TaskResult.SuccessResult;
        }
        
        var text = "";

        try
        {
            text = await result.Content.ReadAsStringAsync();
        }
        catch
        {
            text = "Unknown error";
        }
        
        return TaskResult.FromFailure(text, (int)result.StatusCode);
    }

    public async Task<CommunityNodeTokenExchangeResponse> ExchangeCommunityTokenAsync(NodeManifest manifest)
    {
        if (manifest is null)
            return null;

        await EnsureAuthorityOriginAsync();

        var request = new CommunityNodeTokenExchangeRequest
        {
            NodeId = manifest.NodeId,
            CanonicalOrigin = manifest.CanonicalOrigin
        };

        using var authorityClient = _client.CreateAuthorityHttpClient(includeAuthorization: true);
        var response = await authorityClient.PostAsJsonAsync("api/node/community-token", request);
        if (!response.IsSuccessStatusCode)
        {
            LogError($"Failed to exchange community token for node {manifest.Name}: {await response.Content.ReadAsStringAsync()}");
            return null;
        }

        var result = await response.Content.ReadFromJsonAsync<CommunityNodeTokenExchangeResponse>();
        if (result is null)
            return null;

        return result;
    }

    public async Task<TaskResult> ResendVerificationEmailAsync(RegisterUserRequest request)
    {
        await EnsureAuthorityOriginAsync();

        using var authorityClient = _client.CreateAuthorityHttpClient();
        var response = await authorityClient.PostAsJsonAsync("api/users/resendemail", request);
        var message = await response.Content.ReadAsStringAsync();

        return response.IsSuccessStatusCode
            ? TaskResult.FromSuccess(string.IsNullOrWhiteSpace(message) ? "Verification email sent." : message)
            : TaskResult.FromFailure(message, (int)response.StatusCode);
    }

    public async Task<TaskResult> RequestPasswordResetAsync(string email)
    {
        await EnsureAuthorityOriginAsync();

        using var authorityClient = _client.CreateAuthorityHttpClient();
        var response = await authorityClient.PostAsJsonAsync("api/users/resetpassword", email);
        var message = await response.Content.ReadAsStringAsync();

        return response.IsSuccessStatusCode
            ? TaskResult.FromSuccess(message)
            : TaskResult.FromFailure(message, (int)response.StatusCode);
    }

    public async Task<TaskResult> RecoverPasswordAsync(PasswordRecoveryRequest request)
    {
        await EnsureAuthorityOriginAsync();

        using var authorityClient = _client.CreateAuthorityHttpClient();
        var response = await authorityClient.PostAsJsonAsync("api/users/me/recovery", request);
        var message = await response.Content.ReadAsStringAsync();

        return response.IsSuccessStatusCode
            ? TaskResult.FromSuccess(message)
            : TaskResult.FromFailure(message, (int)response.StatusCode);
    }

    public async Task<TaskResult> VerifyEmailAsync(string code)
    {
        await EnsureAuthorityOriginAsync();

        using var authorityClient = _client.CreateAuthorityHttpClient();
        using var content = new StringContent(code ?? string.Empty);
        var response = await authorityClient.PostAsync("api/users/verify", content);
        var message = await response.Content.ReadAsStringAsync();

        return response.IsSuccessStatusCode
            ? TaskResult.FromSuccess(message)
            : TaskResult.FromFailure(message, (int)response.StatusCode);
    }

    private async Task EnsureAuthorityOriginAsync()
    {
        if (!string.IsNullOrWhiteSpace(_client.AuthorityOrigin))
            return;

        var manifest = await _client.NodeService.FetchNodeManifestAsync();
        _client.SetAuthorityOrigin(
            string.IsNullOrWhiteSpace(manifest?.AuthorityOrigin)
                ? manifest?.CanonicalOrigin ?? _client.BaseAddress
                : manifest.AuthorityOrigin);
    }
    
    /// <summary>
    /// Sets the compliance data for the current user
    /// </summary>
    public async ValueTask<TaskResult> SetComplianceDataAsync(DateTime birthDate, Locality locality)
    {
        var result = await _client.AccountNode.PostAsync($"api/users/me/compliance/{birthDate.ToString("s")}/{locality}", null);
        var taskResult = new TaskResult()
        {
            Success = result.Success,
            Message = result.Message
        };

        return taskResult;
    }
    
    /// <summary>
    /// Returns all the multi-factor authentication methods available to the user
    /// </summary>
    public async Task<List<string>> GetMfaMethodsAsync()
    {
        var response = await _client.AccountNode.GetJsonAsync<List<string>>("api/users/me/multiauth");

        if (!response.Success)
        {
            LogError($"Failed to get multi-auth methods: {response.Message}");
            return [];
        }
        
        return response.Data;
    }
    
    
    /// <summary>
    /// Requests and sets up a multi-factor authentication key
    /// </summary>
    public async Task<TaskResult<CreateAppMultiAuthResponse>> SetupMfaAsync()
    {
        return await _client.AccountNode.PostAsyncWithResponse<CreateAppMultiAuthResponse>($"api/users/me/multiAuth", null);
    }
    
    public async Task<TaskResult> VerifyMfaAsync(string code)
    {
        var result = await _client.AccountNode.PostAsyncWithResponse<bool>($"api/users/me/multiAuth/verify/{code}", null);
        
        if (!result.Success)
            return new TaskResult(false, result.Message);
        
        if (!result.Data)
            return new TaskResult(false, "Invalid code");

        return TaskResult.SuccessResult;
    }
    
    public async Task<TaskResult> RemoveMfaAsync(string password)
    {
        var request = new RemoveMfaRequest()
        {
            Password = password
        };
        
        return await _client.AccountNode.PostAsync("api/users/me/multiAuth/remove", request);
    }

    #region Token Management

    /// <summary>
    /// Gets all tokens for the current user
    /// </summary>
    public async Task<List<AuthToken>> GetMyTokensAsync()
    {
        var response = await _client.AccountNode.GetJsonAsync<List<AuthToken>>("api/users/me/tokens");
        return response.Success ? response.Data : new List<AuthToken>();
    }

    /// <summary>
    /// Revokes a specific token
    /// </summary>
    public async Task<TaskResult> RevokeTokenAsync(string tokenId)
    {
        return await _client.AccountNode.DeleteAsync($"api/users/me/tokens/{tokenId}");
    }

    /// <summary>
    /// Revokes all other tokens (except the current one)
    /// </summary>
    public async Task<TaskResult> RevokeAllOtherTokensAsync()
    {
        return await _client.AccountNode.DeleteAsync("api/users/me/tokens");
    }

    /// <summary>
    /// Logs out the current user (revokes current token)
    /// </summary>
    public async Task<TaskResult> LogoutAsync()
    {
        return await _client.AccountNode.PostAsync("api/users/me/logout", null);
    }

    #endregion
}
