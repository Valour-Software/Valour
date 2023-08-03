using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Valour.Shared.Models;

namespace Valour.Database;

[Table("user_profiles")]
public class UserProfile : ISharedUserProfile
{
    /// <summary>
    /// The user the profile belongs to
    /// </summary>
    [Key]
    [Column("user_id")]
    public long UserId { get; set; }
    
    /// <summary>
    /// The 'headline' is the short top text in the profile
    /// </summary>
    [Column("headline")]
    public string Headline { get; set; }
    
    /// <summary>
    /// The bio of the profile. Supports markdown.
    /// </summary>
    [Column("bio")]
    public string Bio { get; set; }
    
    /// <summary>
    /// The simple border color of the profile.
    /// </summary>
    [Column("border_color")]
    public string BorderColor { get; set; }
    
    /// <summary>
    /// The glow color of the profile
    /// </summary>
    [Column("glow_color")]
    public string GlowColor { get; set; }
    
    /// <summary>
    /// Primary color, used in border and other details
    /// </summary>
    [Column("primary_color")]
    public string PrimaryColor { get; set; }
    
    /// <summary>
    /// Secondary color, used in border and other details
    /// </summary>
    [Column("secondary_color")]
    public string SecondaryColor { get; set; }
    
    /// <summary>
    /// Tertiary color, used in border and other details
    /// </summary>
    [Column("tertiary_color")]
    public string TertiaryColor { get; set; }

    /// <summary>
    /// True if the border should be animated
    /// </summary>
    [Column("anim_border")]
    public bool AnimatedBorder { get; set; }
}