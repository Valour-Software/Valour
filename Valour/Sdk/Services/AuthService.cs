using Valour.Sdk.Client;
using Valour.Shared;
using Valour.Shared.Models;
using Valour.Shared.Utilities;

namespace Valour.SDK.Services;

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
    public async Task<TaskResult> FetchToken(string email, string password)
    {
        TokenRequest request = new()
        {
            Email = email,
            Password = password
        };
        
        var response = await _client.PrimaryNode.PostAsyncWithResponse<AuthToken>($"api/users/token", request);

        if (response.Success)
        {
            var token = response.Data.Id;
            _token = token;
        }

        return response.WithoutData();
    }

    public async Task<TaskResult> LoginAsync(string email, string password)
    {
        var tokenResult = await FetchToken(email, password);
        if (!tokenResult.Success)
            return tokenResult;
        
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
        
        var response = await _client.PrimaryNode.GetJsonAsync<User>($"api/users/self");

        if (!response.Success)
            return response.WithoutData();

        _client.Self = response.Data;
        
        LoggedIn?.Invoke(_client.Self);

        return new TaskResult(true, "Success");
    }
}