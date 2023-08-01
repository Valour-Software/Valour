namespace Valour.Server.Models;

public class UserProfile
{
    /// <summary>
    /// The user the profile belongs to
    /// </summary>
    public long UserId { get; set; }
    
    /// <summary>
    /// The 'headline' is the short top text in the profile
    /// </summary>
    public string Headline { get; set; }
    
    /// <summary>
    /// The bio of the profile. Supports markdown.
    /// </summary>
    public string Bio { get; set; }
    
    /// <summary>
    /// The simple border color of the profile.
    /// </summary>
    public string BorderColor { get; set; }
    
    /// <summary>
    /// The advanced CSS style of the border background of the profile.
    /// If the user has access, this will override the simple border color.
    /// Can be solid color or gradient (may support images in the future).
    /// </summary>
    public string AdvancedBorderBackground { get; set; }
    
    /// <summary>
    /// True if the border should be animated
    /// </summary>
    public bool AnimatedBorder { get; set; }
}