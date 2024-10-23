using Valour.Sdk.Client;
using Valour.Shared;
using Valour.Shared.Models;
using Valour.Shared.Utilities;

namespace Valour.SDK.Services;

/// <summary>
/// Handles tokens and authentication
/// </summary>
public static class AuthService
{
    /// <summary>
    /// Run when the user logs in
    /// </summary>
    public static HybridEvent<User> LoggedIn;
    
    /// <summary>
    /// The token for this client instance
    /// </summary>
    public static string Token => _token;

    /// <summary>
    /// The internal token for this client
    /// </summary>
    private static string _token;
    
    /// <summary>
    /// Gets the Token for the client
    /// </summary>
    public static async Task<TaskResult> FetchToken(string email, string password)
    {
        TokenRequest request = new()
        {
            Email = email,
            Password = password
        };
        
        var response = await ValourClient.PostAsyncWithResponse<AuthToken>($"api/users/token", request);

        if (response.Success)
        {
            var token = response.Data.Id;
            _token = token;
        }

        return response.WithoutData();
    }

    public static async Task<TaskResult> LoginAsync(string email, string password)
    {
        var tokenResult = await FetchToken(email, password);
        if (!tokenResult.Success)
            return tokenResult;
        
        return await LoginAsync(Token);
    }
    
    public static async Task<TaskResult> LoginAsync(string token)
    {
        // Ensure any existing auth headers are removed
        if (ValourClient.Http.DefaultRequestHeaders.Contains("authorization"))
        {
            ValourClient.Http.DefaultRequestHeaders.Remove("authorization");
        }
        
        // Add auth header to main http client so we never have to do that again
        ValourClient.Http.DefaultRequestHeaders.Add("authorization", Token);
        
        var response = await ValourClient.GetJsonAsync<User>($"api/users/self");

        if (!response.Success)
            return response.WithoutData();

        ValourClient.Self = response.Data;
        
        LoggedIn?.Invoke(ValourClient.Self);

        return new TaskResult(true, "Success");
    }
}