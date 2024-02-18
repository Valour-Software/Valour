using System.ComponentModel.DataAnnotations.Schema;

namespace Valour.Database.Themes;

[Table("themes")]
public class Theme
{
    [ForeignKey("AuthorId")]
    public virtual User Author { get; set; }
    
    [Column("id")]
    public long Id { get; set; }
    
    [Column("author_id")]
    public long AuthorId { get; set; }
    
    [Column("name")]
    public string Name { get; set; }
    
    [Column("description")]
    public string Description { get; set; }
    
    
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
}