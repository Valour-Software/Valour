namespace Valour.Sdk.Models;

/// <summary>
/// Response model for OAuth authorization
/// </summary>
public class OauthAuthorizationResponse
{
    /// <summary>
    /// The authorization code to be exchanged for an access token
    /// </summary>
    public string Code { get; set; }

    /// <summary>
    /// The state parameter that was provided in the authorization request
    /// </summary>
    public string? State { get; set; }

    /// <summary>
    /// The node name that processed the authorization
    /// </summary>
    public string Node { get; set; }

    /// <summary>
    /// The complete redirect URL with all parameters
    /// </summary>
    public string RedirectUrl { get; set; }
}
