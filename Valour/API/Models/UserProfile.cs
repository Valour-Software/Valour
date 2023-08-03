using Valour.Shared.Models;

namespace Valour.Api.Models;

public class UserProfile : ISharedUserProfile
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
    /// The glow color of the profile
    /// </summary>
    public string GlowColor { get; set; }
    
    /// <summary>
    /// Primary color, used in border and other details
    /// </summary>
    public string PrimaryColor { get; set; }
    
    /// <summary>
    /// Secondary color, used in border and other details
    /// </summary>
    public string SecondaryColor { get; set; }
    
    /// <summary>
    /// Tertiary color, used in border and other details
    /// </summary>
    public string TertiaryColor { get; set; }
    
    /// <summary>
    /// True if the border should be animated
    /// </summary>
    public bool AnimatedBorder { get; set; }
}