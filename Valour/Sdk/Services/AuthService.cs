using System.Net.Http.Json;
using System.Text.Json;
using Valour.Sdk.Client;
using Valour.Shared;
using Valour.Shared.Models;
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
        TokenRequest request = new()
        {
            Email = email,
            Password = password,
            MultiFactorCode = multiFactorCode
        };

        var httpContent = JsonContent.Create(request);
        var response = await _client.Http.PostAsync($"api/users/token", httpContent);
        
        
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
        // Ensure any existing auth headers are removed
        if (_client.Http.DefaultRequestHeaders.Contains("authorization"))
        {
            _client.Http.DefaultRequestHeaders.Remove("authorization");
        }
        
        // Add auth header to main http client so we never have to do that again
        _client.Http.DefaultRequestHeaders.Add("authorization", Token);

        if (_client.PrimaryNode is null)
        {
            var nodeResult = await _client.NodeService.SetupPrimaryNodeAsync();
            if (!nodeResult.Success)
                return nodeResult;
        }
        else
        {
            // Update the token if it's already been set
            _client.PrimaryNode.UpdateToken();
        }

        var response = await _client.PrimaryNode!.GetJsonAsync<User>($"api/users/me");

        if (!response.Success)
            return response.WithoutData();

        _client.Me = response.Data.Sync(_client);
        
        LoggedIn?.Invoke(_client.Me);

        return new TaskResult(true, "Success");
    }

    public async Task<TaskResult> RegisterAsync(RegisterUserRequest request)
    {
        var content = JsonContent.Create(request);
        var result = await _client.Http.PostAsync("api/users/register", content);

        if (result.IsSuccessStatusCode)
        {
            return TaskResult.SuccessResult;
        }
        
        var text = "";

        try
        {
            text = await result.Content.ReadAsStringAsync();
        } catch (Exception ex)
        {
            text = "Unknown error";
        }
        
        return TaskResult.FromFailure(text, (int)result.StatusCode);
    }
    
    /// <summary>
    /// Sets the compliance data for the current user
    /// </summary>
    public async ValueTask<TaskResult> SetComplianceDataAsync(DateTime birthDate, Locality locality)
    {
        var result = await _client.PrimaryNode.PostAsync($"api/users/me/compliance/{birthDate.ToString("s")}/{locality}", null);
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
        var response = await _client.PrimaryNode.GetJsonAsync<List<string>>("api/users/me/multiauth");

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
        return await _client.PrimaryNode.PostAsyncWithResponse<CreateAppMultiAuthResponse>($"api/users/me/multiAuth", null);
    }
    
    public async Task<TaskResult> VerifyMfaAsync(string code)
    {
        var result = await _client.PrimaryNode.PostAsyncWithResponse<bool>($"api/users/me/multiAuth/verify/{code}", null);
        
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
        
        return await _client.PrimaryNode.PostAsync("api/users/me/multiAuth/remove", request);
    }
}