using Valour.Sdk.Client;
using Valour.Sdk.Services;
using Valour.Sdk.Requests;
using Valour.Shared.Models;

namespace Valour.Sdk.Examples;

/// <summary>
/// Examples demonstrating how to use the OAuth SDK helpers
/// </summary>
public static class OauthExamples
{
    /// <summary>
    /// Example: Creating a simple OAuth app
    /// </summary>
    public static async Task<OauthApp> CreateSimpleAppExample(ValourClient client)
    {
        // Using the high-level helper
        var result = await client.OauthHelper.CreateSimpleAppAsync(
            name: "My Awesome App",
            redirectUrl: "https://myapp.com/oauth/callback"
        );

        if (result.Success)
        {
            Console.WriteLine($"App created successfully! ID: {result.Data.Id}");
            Console.WriteLine($"Secret: {result.Data.Secret}");
            return result.Data;
        }
        else
        {
            Console.WriteLine($"Failed to create app: {result.Message}");
            return null;
        }
    }

    /// <summary>
    /// Example: Creating an OAuth app with custom image
    /// </summary>
    public static async Task<OauthApp> CreateAppWithImageExample(ValourClient client)
    {
        // Using the high-level helper with custom image upload
        using var imageStream = File.OpenRead("path/to/logo.png");
        var result = await client.OauthHelper.CreateAppWithImageAsync(
            name: "My App with Custom Icon",
            redirectUrl: "https://myapp.com/oauth/callback",
            imageStream: imageStream,
            fileName: "logo.png"
        );

        if (result.Success)
        {
            Console.WriteLine($"App created with custom image! ID: {result.Data.Id}");
            return result.Data;
        }
        else
        {
            Console.WriteLine($"Failed to create app: {result.Message}");
            return null;
        }
    }

    /// <summary>
    /// Example: Using the detailed OAuth service for more control
    /// </summary>
    public static async Task<OauthApp> CreateAppDetailedExample(ValourClient client)
    {
        var request = new CreateOauthAppRequest
        {
            Name = "My Detailed App",
            RedirectUrl = "https://myapp.com/oauth/callback"
        };

        var result = await client.OauthService.CreateAppAsync(request);

        if (result.Success)
        {
            Console.WriteLine($"App created! ID: {result.Data.Id}");
            return result.Data;
        }
        else
        {
            Console.WriteLine($"Failed to create app: {result.Message}");
            return null;
        }
    }

    /// <summary>
    /// Example: Updating an OAuth app
    /// </summary>
    public static async Task UpdateAppExample(ValourClient client, long appId)
    {
        // Update just the redirect URL
        var result = await client.OauthHelper.UpdateRedirectUrlAsync(
            appId: appId,
            newRedirectUrl: "https://myapp.com/new-callback"
        );

        if (result.Success)
        {
            Console.WriteLine("Redirect URL updated successfully!");
        }
        else
        {
            Console.WriteLine($"Failed to update: {result.Message}");
        }
    }

    /// <summary>
    /// Example: Getting all user's OAuth apps
    /// </summary>
    public static async Task ListAppsExample(ValourClient client)
    {
        var apps = await client.OauthHelper.GetMyAppsAsync();

        Console.WriteLine($"You have {apps.Count} OAuth apps:");
        foreach (var app in apps)
        {
            Console.WriteLine($"- {app.Name} (ID: {app.Id}, Uses: {app.Uses})");
        }
    }

    /// <summary>
    /// Example: Complete OAuth flow for a third-party application
    /// </summary>
    public static async Task CompleteOauthFlowExample(ValourClient client, long clientId, string clientSecret)
    {
        // Step 1: Generate authorization URL
        var authUrl = client.OauthHelper.GetAuthorizationUrlWithAutoState(
            clientId: clientId,
            redirectUri: "https://myapp.com/oauth/callback",
            scope: 0 // No special permissions
        );

        Console.WriteLine($"Authorization URL: {authUrl}");
        Console.WriteLine("User should visit this URL to authorize your app");

        // Step 2: After user authorizes, you'll receive a code
        // This is typically handled in your web application
        string receivedCode = "example_code_from_callback";
        string receivedState = "example_state_from_callback";

        // Step 3: Exchange code for token
        var tokenResult = await client.OauthHelper.ExchangeCodeAsync(
            clientId: clientId,
            clientSecret: clientSecret,
            code: receivedCode,
            redirectUri: "https://myapp.com/oauth/callback",
            state: receivedState
        );

        if (tokenResult.Success)
        {
            Console.WriteLine($"Access token obtained: {tokenResult.Data.Id}");
            Console.WriteLine($"Token expires: {tokenResult.Data.TimeExpires}");
        }
        else
        {
            Console.WriteLine($"Failed to get token: {tokenResult.Message}");
        }
    }

    /// <summary>
    /// Example: Validating OAuth parameters before use
    /// </summary>
    public static void ValidationExample(ValourClient client)
    {
        // Validate app creation parameters
        var appValidation = client.OauthHelper.ValidateAppCreation(
            name: "My App",
            redirectUrl: "https://myapp.com/callback"
        );

        if (appValidation.Success)
        {
            Console.WriteLine("App parameters are valid!");
        }
        else
        {
            Console.WriteLine($"App validation failed: {appValidation.Message}");
        }

        // Validate authorization parameters
        var authValidation = client.OauthHelper.ValidateAuthorizationParams(
            clientId: 12345,
            redirectUri: "https://myapp.com/callback"
        );

        if (authValidation.Success)
        {
            Console.WriteLine("Authorization parameters are valid!");
        }
        else
        {
            Console.WriteLine($"Authorization validation failed: {authValidation.Message}");
        }
    }

    /// <summary>
    /// Example: Deleting an OAuth app
    /// </summary>
    public static async Task DeleteAppExample(ValourClient client, long appId)
    {
        var result = await client.OauthHelper.DeleteAppAsync(appId);

        if (result.Success)
        {
            Console.WriteLine("OAuth app deleted successfully!");
        }
        else
        {
            Console.WriteLine($"Failed to delete app: {result.Message}");
        }
    }

    /// <summary>
    /// Example: Getting public information about an OAuth app
    /// </summary>
    public static async Task GetPublicAppInfoExample(ValourClient client, long appId)
    {
        var appInfo = await client.OauthHelper.GetAppPublicInfoAsync(appId);

        if (appInfo != null)
        {
            Console.WriteLine($"App: {appInfo.Name}");
            Console.WriteLine($"Image: {appInfo.ImageUrl}");
            Console.WriteLine($"Redirect URL: {appInfo.RedirectUrl}");
        }
        else
        {
            Console.WriteLine("App not found or not accessible");
        }
    }

    /// <summary>
    /// Example: Web application OAuth flow
    /// This shows how to handle OAuth in a web application
    /// </summary>
    public static async Task WebAppOauthFlowExample(ValourClient client, long clientId, string clientSecret)
    {
        // In a web app, you would typically:
        
        // 1. Generate state for security
        var state = client.OauthHelper.GenerateSecureState();
        
        // 2. Store state in session/cache for verification
        // Session["oauth_state"] = state;
        
        // 3. Generate authorization URL
        var authUrl = client.OauthHelper.GetAuthorizationUrl(
            clientId: clientId,
            redirectUri: "https://myapp.com/oauth/callback",
            scope: 0,
            state: state
        );
        
        // 4. Redirect user to authorization URL
        // Response.Redirect(authUrl);
        
        // 5. In your callback endpoint, you would:
        // - Verify the state parameter matches what you stored
        // - Extract the authorization code from the callback
        // - Exchange the code for a token
        
        Console.WriteLine($"Authorization URL: {authUrl}");
        Console.WriteLine($"State: {state}");
    }
}
