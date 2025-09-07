using System.ComponentModel.DataAnnotations;

namespace Valour.Sdk.Requests;

/// <summary>
/// Request model for creating a new OAuth application
/// </summary>
public class CreateOauthAppRequest
{
    /// <summary>
    /// The name of the OAuth application
    /// Must be between 1 and 32 characters
    /// </summary>
    [Required]
    [StringLength(32, MinimumLength = 1, ErrorMessage = "App name must be between 1 and 32 characters")]
    public string Name { get; set; }

    /// <summary>
    /// The redirect URL for OAuth authorization
    /// This is where users will be redirected after authorization
    /// </summary>
    [Required]
    [Url(ErrorMessage = "Redirect URL must be a valid URL")]
    public string RedirectUrl { get; set; }

    // Note: Images should be uploaded separately using the UploadAppImageAsync method
    // This ensures security by using the CDN upload system
}
