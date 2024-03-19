using Valour.Shared.Models.Themes;

namespace Valour.Server.Models.Themes;

public class Theme : ISharedTheme
{
    public long Id { get; set; }
    public long AuthorId { get; set; }
    public string Name { get; set; }
    public string Description { get; set; }
    
    public bool HasCustomBanner { get; set; }
    
    public bool HasAnimatedBanner { get; set; }
    public bool Published { get; set; }
    
    public string FontColor { get; set; }
    public string FontAltColor { get; set; }
    public string LinkColor { get; set; }
    
    public string MainColor1 { get; set; }
    public string MainColor2 { get; set; }
    public string MainColor3 { get; set; }
    public string MainColor4 { get; set; }
    public string MainColor5 { get; set; }
    
    public string TintColor { get; set; }
    
    public string VibrantPurple { get; set; }
    public string VibrantBlue { get; set; }
    public string VibrantCyan { get; set; }
    
    public string PastelCyan { get; set; }
    public string PastelCyanPurple { get; set; }
    public string PastelPurple { get; set; }
    public string PastelRed { get; set; }
    
    public string CustomCss { get; set; }
}