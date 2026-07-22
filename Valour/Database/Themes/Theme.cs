using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json.Serialization;

namespace Valour.Database.Themes;

[Table("themes")]
public class Theme
{
    [ForeignKey("AuthorId")]
    public virtual User Author { get; set; }

    [InverseProperty("Theme")]
    [JsonIgnore]
    public virtual ICollection<ThemeVote> ThemeVotes { get; set; }

    [InverseProperty("Theme")]
    [JsonIgnore]
    public virtual ICollection<ThemeAsset> ThemeAssets { get; set; }

    [Column("id")]
    public long Id { get; set; }

    [Column("author_id")]
    public long AuthorId { get; set; }

    [Column("name")]
    public string Name { get; set; }

    [Column("description")]
    public string Description { get; set; }

    [Column("custom_banner")]
    public bool HasCustomBanner { get; set; }

    [Column("animated_banner")]
    public bool HasAnimatedBanner { get; set; }

    [Column("published")]
    public bool Published { get; set; }


    [Column("font_family")]
    public string FontFamily { get; set; }

    [Column("radius_xs")]
    public string RadiusXs { get; set; }

    [Column("radius_sm")]
    public string RadiusSm { get; set; }

    [Column("radius_md")]
    public string RadiusMd { get; set; }

    [Column("radius_lg")]
    public string RadiusLg { get; set; }

    [Column("radius_xl")]
    public string RadiusXl { get; set; }

    [Column("radius_full")]
    public string RadiusFull { get; set; }

    [Column("font_color")]
    public string FontColor { get; set; }

    [Column("font_alt_color")]
    public string FontAltColor { get; set; }

    [Column("link_color")]
    public string LinkColor { get; set; }


    [Column("main_color_1")]
    public string MainColor1 { get; set; }

    [Column("main_color_2")]
    public string MainColor2 { get; set; }

    [Column("main_color_3")]
    public string MainColor3 { get; set; }

    [Column("main_color_4")]
    public string MainColor4 { get; set; }

    [Column("main_color_5")]
    public string MainColor5 { get; set; }


    [Column("tint_color")]
    public string TintColor { get; set; }


    [Column("vibrant_purple")]
    public string VibrantPurple { get; set; }

    [Column("vibrant_blue")]
    public string VibrantBlue { get; set; }

    [Column("vibrant_cyan")]
    public string VibrantCyan { get; set; }


    [Column("pastel_cyan")]
    public string PastelCyan { get; set; }

    [Column("pastel_cyan_purple")]
    public string PastelCyanPurple { get; set; }

    [Column("pastel_purple")]
    public string PastelPurple { get; set; }

    [Column("pastel_red")]
    public string PastelRed { get; set; }

    [Column("custom_css")]
    public string CustomCss { get; set; }
}
