namespace Valour.Shared.Models;

public interface ISharedUserProfile
{
    /// <summary>
    /// The user the profile belongs to
    /// </summary>
    long UserId { get; set; }
    
    /// <summary>
    /// The 'headline' is the short top text in the profile
    /// </summary>
    string Headline { get; set; }
    
    /// <summary>
    /// The bio of the profile. Supports markdown.
    /// </summary>
    string Bio { get; set; }
    
    /// <summary>
    /// The simple border color of the profile.
    /// </summary>
    string BorderColor { get; set; }
    
    /// <summary>
    /// The advanced CSS style of the border background of the profile.
    /// If the user has access, this will override the simple border color.
    /// Can be solid color or gradient (may support images in the future).
    /// </summary>
    string AdvancedBorderBackground { get; set; }
    
    /// <summary>
    /// True if the border should be animated
    /// </summary>
    bool AnimatedBorder { get; set; }
}