using System.ComponentModel.DataAnnotations;

namespace Valour.Sdk.Requests;

/// <summary>
/// Request model for exchanging authorization code for access token
/// </summary>
public class OauthTokenRequest
{
    /// <summary>
    /// The OAuth app's client ID
    /// </summary>
    [Required]
    public long ClientId { get; set; }

    /// <summary>
    /// The OAuth app's client secret
    /// </summary>
    [Required]
    public string ClientSecret { get; set; }

    /// <summary>
    /// The grant type (currently only "authorization_code" is supported)
    /// </summary>
    [Required]
    public string GrantType { get; set; } = "authorization_code";

    /// <summary>
    /// The authorization code received from the authorization endpoint
    /// </summary>
    [Required]
    public string Code { get; set; }

    /// <summary>
    /// The redirect URI that must match the one used in authorization
    /// </summary>
    [Required]
    [Url(ErrorMessage = "Redirect URI must be a valid URL")]
    public string RedirectUri { get; set; }

    /// <summary>
    /// The state parameter that must match the one used in authorization
    /// </summary>
    public string? State { get; set; }
}
