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
    public async Task<TaskResult<string>> FetchToken(string email, string password)
    {
        TokenRequest request = new()
        {
            Email = email,
            Password = password
        };

        var httpContent = JsonContent.Create(request);
        var response = await _client.Http.PostAsync($"api/users/token", httpContent);
        
        
        if (response.IsSuccessStatusCode)
        {
            var token = await response.Content.ReadFromJsonAsync<AuthToken>();
            _token = token.Id;

            return TaskResult<string>.FromData(_token);
        }

        return new TaskResult<string>()
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

    public async Task<TaskResult> LoginAsync(string email, string password)
    {
        var tokenResult = await FetchToken(email, password);
        if (!tokenResult.Success)
            return tokenResult.WithoutData();
        
        return await LoginAsync();
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

        var response = await _client.PrimaryNode!.GetJsonAsync<User>($"api/users/me");

        if (!response.Success)
            return response.WithoutData();

        _client.Me = _client.Cache.Sync(response.Data);
        
        LoggedIn?.Invoke(_client.Me);

        return new TaskResult(true, "Success");
    }

    public Task<TaskResult> RegisterAsync(RegisterUserRequest request)
    {
        return _client.PrimaryNode.PostAsync("api/users/register", request);
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
}