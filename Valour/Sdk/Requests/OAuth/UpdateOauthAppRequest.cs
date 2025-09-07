using System.ComponentModel.DataAnnotations;

namespace Valour.Sdk.Requests;

/// <summary>
/// Request model for updating an existing OAuth application
/// </summary>
public class UpdateOauthAppRequest
{
    /// <summary>
    /// The new name for the OAuth application
    /// Must be between 1 and 32 characters
    /// </summary>
    [StringLength(32, MinimumLength = 1, ErrorMessage = "App name must be between 1 and 32 characters")]
    public string? Name { get; set; }

    /// <summary>
    /// The new redirect URL for OAuth authorization
    /// This is where users will be redirected after authorization
    /// </summary>
    [Url(ErrorMessage = "Redirect URL must be a valid URL")]
    public string? RedirectUrl { get; set; }

    // Note: Images should be uploaded separately using the UploadAppImageAsync method
    // This ensures security by using the CDN upload system
}
