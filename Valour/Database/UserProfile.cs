using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Valour.Database;

[Table("user_profiles")]
public class UserProfile
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
    /// The advanced CSS style of the border background of the profile.
    /// If the user has access, this will override the simple border color.
    /// Can be solid color or gradient (may support images in the future).
    /// </summary>
    [Column("adv_border_bg")]
    public string AdvancedBorderBackground { get; set; }
    
    /// <summary>
    /// True if the border should be animated
    /// </summary>
    [Column("anim_border")]
    public bool AnimatedBorder { get; set; }
}