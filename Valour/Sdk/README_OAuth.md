# Valour OAuth Implementation

This document describes the OAuth implementation in Valour and how to use the SDK helpers for OAuth operations.

## Overview

Valour implements OAuth 2.0 authorization code flow, allowing third-party applications to access user accounts with explicit permission. The implementation includes:

- OAuth app management (create, update, delete)
- Authorization flow handling
- Token exchange
- Comprehensive SDK helpers

## Architecture

### Components

1. **OAuth API** (`Server/Api/OauthAPI.cs`) - Server-side OAuth endpoints
2. **OAuth Service** (`Sdk/Services/OauthService.cs`) - Low-level SDK service
3. **OAuth Helper** (`Sdk/Services/OauthHelper.cs`) - High-level SDK helper
4. **Request Models** (`Sdk/Requests/`) - Structured request objects
5. **Response Models** (`Sdk/Models/`) - OAuth response objects

### Data Models

- `OauthApp` - OAuth application data
- `PublicOauthAppData` - Public app information
- `AuthorizeModel` - Authorization request data
- `AuthToken` - Access token data

## API Endpoints

### App Management
- `POST /api/oauth/app` - Create OAuth app
- `GET /api/oauth/app/{id}` - Get OAuth app (owner only)
- `DELETE /api/oauth/app/{id}` - Delete OAuth app
- `GET /api/oauth/app/public/{id}` - Get public app data
- `GET /api/users/apps` - Get user's OAuth apps

### OAuth Flow
- `POST /api/oauth/authorize` - Initiate authorization
- `GET /api/oauth/token` - Exchange code for token

## SDK Usage

### Basic Setup

```csharp
var client = new ValourClient("https://app.valour.gg");
await client.InitializeUser("your-token");

// Access OAuth services
var oauthService = client.OauthService;    // Low-level service
var oauthHelper = client.OauthHelper;      // High-level helper
```

### Creating OAuth Apps

#### Simple Creation
```csharp
var result = await client.OauthHelper.CreateSimpleAppAsync(
    name: "My App",
    redirectUrl: "https://myapp.com/oauth/callback"
);

if (result.Success)
{
    var app = result.Data;
    Console.WriteLine($"App ID: {app.Id}");
    Console.WriteLine($"Secret: {app.Secret}");
}
```

#### Detailed Creation
```csharp
var request = new CreateOauthAppRequest
{
    Name = "My App",
    RedirectUrl = "https://myapp.com/oauth/callback"
};

var result = await client.OauthService.CreateAppAsync(request);
```

### Managing OAuth Apps

#### List User's Apps
```csharp
var apps = await client.OauthHelper.GetMyAppsAsync();
foreach (var app in apps)
{
    Console.WriteLine($"{app.Name} (ID: {app.Id}, Uses: {app.Uses})");
}
```

#### Update App Properties
```csharp
// Update redirect URL
await client.OauthHelper.UpdateRedirectUrlAsync(appId, "https://new-callback.com");

// Update app name
await client.OauthHelper.UpdateAppNameAsync(appId, "New App Name");
```

#### Image Management
```csharp
// Upload app image (secure CDN upload)
using var imageStream = File.OpenRead("path/to/logo.png");
var uploadResult = await client.OauthHelper.UploadAppImageAsync(appId, imageStream, "logo.png");

if (uploadResult.Success)
{
    Console.WriteLine($"Image uploaded: {uploadResult.Data}");
}
```

// Update app name
await client.OauthHelper.UpdateAppNameAsync(appId, "New App Name");

// Update app image
await client.OauthHelper.UpdateAppImageAsync(appId, "https://new-icon.png");
```

#### Delete App
```csharp
var result = await client.OauthHelper.DeleteAppAsync(appId);
if (result.Success)
{
    Console.WriteLine("App deleted successfully");
}
```

### OAuth Flow Implementation

#### For Web Applications

1. **Generate Authorization URL**
```csharp
var authUrl = client.OauthHelper.GetAuthorizationUrlWithAutoState(
    clientId: yourAppId,
    redirectUri: "https://myapp.com/oauth/callback",
    scope: 0
);

// Redirect user to authUrl
Response.Redirect(authUrl);
```

2. **Handle Callback**
```csharp
// In your callback endpoint
var code = Request.Query["code"];
var state = Request.Query["state"];

// Verify state matches what you stored
if (state != Session["oauth_state"])
{
    return BadRequest("Invalid state parameter");
}

// Exchange code for token
var tokenResult = await client.OauthHelper.ExchangeCodeAsync(
    clientId: yourAppId,
    clientSecret: yourAppSecret,
    code: code,
    redirectUri: "https://myapp.com/oauth/callback",
    state: state
);

if (tokenResult.Success)
{
    var token = tokenResult.Data;
    // Store token securely for future API calls
}
```

#### For Desktop/Mobile Applications

```csharp
// Complete flow in one call
var tokenResult = await client.OauthHelper.CompleteOauthFlowAsync(
    clientId: yourAppId,
    clientSecret: yourAppSecret,
    redirectUri: "https://myapp.com/oauth/callback"
);

if (tokenResult.Success)
{
    var token = tokenResult.Data;
    // Use token for API calls
}
```

### Validation

#### Validate App Creation Parameters
```csharp
var validation = client.OauthHelper.ValidateAppCreation(
    name: "My App",
    redirectUrl: "https://myapp.com/callback"
);

if (!validation.Success)
{
    Console.WriteLine($"Validation failed: {validation.Message}");
}
```

#### Validate Authorization Parameters
```csharp
var validation = client.OauthHelper.ValidateAuthorizationParams(
    clientId: 12345,
    redirectUri: "https://myapp.com/callback"
);
```

## Security Considerations

### OAuth App Security
- **Client Secret**: Keep your app's client secret secure and never expose it in client-side code
- **Redirect URLs**: Always validate redirect URLs to prevent open redirect attacks
- **State Parameter**: Always use and verify the state parameter to prevent CSRF attacks
- **Image Uploads**: OAuth app images must be uploaded through the secure CDN system, not via URLs
  - This prevents malicious image URLs and ensures all images are properly validated
  - Use `UploadAppImageAsync()` method for secure image uploads
  - Images are processed and optimized by the CDN system

### Token Security
- **Token Storage**: Store access tokens securely (encrypted at rest)
- **Token Expiration**: Tokens expire after 7 days, implement refresh logic if needed
- **Token Scope**: Only request the minimum scope needed for your application

### Best Practices
1. **HTTPS Only**: Always use HTTPS for OAuth flows
2. **Validate Inputs**: Validate all OAuth parameters before use
3. **Error Handling**: Implement proper error handling for OAuth failures
4. **Logging**: Log OAuth events for security monitoring
5. **Rate Limiting**: Implement rate limiting for OAuth endpoints

## Limitations

### Current Limitations
- Only authorization code grant type is supported
- Tokens expire after 7 days (no refresh token support)
- Maximum 10 OAuth apps per user
- Limited scope system (currently only basic permissions)

### Known Issues
- OAuth request cache needs cleanup mechanism (TODO in code)
- Uses planet name validation for OAuth app names (may be too restrictive)
- No OAuth app rate limiting implemented

## Error Handling

### Common Error Scenarios

1. **Invalid Client ID/Secret**
```csharp
// Error: "Parameters are invalid"
// Solution: Verify client ID and secret are correct
```

2. **Redirect URL Mismatch**
```csharp
// Error: "Client redirect url does not match given url"
// Solution: Ensure redirect URL exactly matches registered URL
```

3. **Invalid Authorization Code**
```csharp
// Error: "Parameters are invalid"
// Solution: Codes are single-use and expire quickly
```

4. **App Limit Reached**
```csharp
// Error: "There is currently a 10 app limit!"
// Solution: Delete unused apps or contact support
```

### Error Response Format
```csharp
var result = await client.OauthHelper.CreateSimpleAppAsync(name, redirectUrl);
if (!result.Success)
{
    Console.WriteLine($"Error: {result.Message}");
    // Handle error appropriately
}
```

## Examples

See `Sdk/Examples/OauthExamples.cs` for comprehensive examples of all OAuth operations.

## Migration from Old Implementation

If you're using the old OAuth implementation:

1. **Replace direct API calls** with SDK helper methods
2. **Use request models** instead of raw objects
3. **Implement proper validation** using the new validation methods
4. **Update error handling** to use the new TaskResult pattern

## Future Enhancements

### Planned Features
- Refresh token support
- More granular scope system
- OAuth app analytics
- Webhook support for OAuth events
- OAuth app marketplace

### Potential Improvements
- Implement OAuth request cache cleanup
- Add OAuth app rate limiting
- Create dedicated OAuth app name validation
- Add OAuth flow testing tools
- Implement OAuth app templates

## Support

For issues with the OAuth implementation:
1. Check the error messages for specific guidance
2. Review the validation methods for parameter requirements
3. Ensure you're following security best practices
4. Contact the Valour development team for complex issues
