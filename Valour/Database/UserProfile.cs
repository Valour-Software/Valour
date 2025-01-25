using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;
using Valour.Shared.Models;

namespace Valour.Database;

[Table("user_profiles")]
public class UserProfile : ISharedUserProfile
{
    /// <summary>
    /// The user the profile belongs to
    /// </summary>
    [Key]
    [Column("id")]
    public long Id { get; set; }
    
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
    /// The color of the main text
    /// </summary>
    [Column("text_color")]
    public string TextColor { get; set; }
    
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
    
    /// <summary>
    /// The background image for the profile (should be 300x400)
    /// </summary>
    [Column("bg_image")]
    public string BackgroundImage { get; set; }

    public static void SetUpDDModel(ModelBuilder builder)
    {
        builder.Entity<UserProfile>(e =>
        {
            // TOtable
            e.ToTable("user_profiles");
            
            // Key
            e.HasKey(x => x.Id);
            
            // Properties

            e.Property(x => x.Headline)
                .HasColumnName("headline");

            e.Property(x => x.Bio)
                .HasColumnName("bio");

            e.Property(x => x.BorderColor)
                .HasColumnName("border_color");

            e.Property(x => x.GlowColor)
                .HasColumnName("glow_color");

            e.Property(x => x.TextColor)
                .HasColumnName("text_color");

            e.Property(x => x.PrimaryColor)
                .HasColumnName("primary_color");
            
            e.Property(x => x.SecondaryColor)
                .HasColumnName("secondary_color");

            e.Property(x => x.TertiaryColor)
                .HasColumnName("tertiary_color");
            
            e.Property(x => x.AnimatedBorder)
                .HasColumnName("animated_border");
            
            e.Property(x => x.BackgroundImage)
                .HasColumnName("background_image");
            
            // Relationships
            
            // Indices
            
            e.HasIndex(x => x.Id)
                .IsUnique();
            

        });
    }
}