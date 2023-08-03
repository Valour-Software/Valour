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
    /// The background glow color of the profile.
    /// </summary>
    string GlowColor { get; set; }
    
    /// <summary>
    /// Primary color, used in border and other details
    /// </summary>
    string PrimaryColor { get; set; }
    
    /// <summary>
    /// Secondary color, used in border and other details
    /// </summary>
    string SecondaryColor { get; set; }
    
    /// <summary>
    /// Tertiary color, used in border and other details
    /// </summary>
    string TertiaryColor { get; set; }
    
    /// <summary>
    /// True if the border should be animated
    /// </summary>
    bool AnimatedBorder { get; set; }
}