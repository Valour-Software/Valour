namespace Valour.Shared.Models.Themes;

public interface ISharedTheme : ISharedThemeMeta
{
    public bool Published { get; set; }
    
    public string FontColor { get; set; }
    public string FontAltColor { get; set; }
    public string LinkColor { get; set; }
    
    // public string MainColor1 { get; set; }
    public string MainColor2 { get; set; }
    public string MainColor3 { get; set; }
    public string MainColor4 { get; set; }
    public string MainColor5 { get; set; }
    
    public string TintColor { get; set; }
    
    public string VibrantPurple { get; set; }
    public string VibrantBlue { get; set; }
    public string VibrantCyan { get; set; }
    
    // public string PastelCyan { get; set; }
    public string PastelCyanPurple { get; set; }
    public string PastelPurple { get; set; }
    public string PastelRed { get; set; }
    
    public string CustomCss { get; set; }
    
    private static readonly Dictionary<ThemeBannerFormat, string> BannerFormatMap = new()
    {
        { ThemeBannerFormat.Webp, "600x300.webp" },
        { ThemeBannerFormat.Jpeg, "600x300.jpg" },
        { ThemeBannerFormat.WebpAnimated, "anim-600x300.webp" },
        { ThemeBannerFormat.Gif, "anim-600x300.gif" },
    };
    
    private static readonly HashSet<ThemeBannerFormat> AnimatedFormats = new()
    {
        ThemeBannerFormat.Gif,
        ThemeBannerFormat.WebpAnimated,
    };
    
    private static readonly Dictionary<ThemeBannerFormat, ThemeBannerFormat> AnimatedToStaticBackup = new()
    {
        { ThemeBannerFormat.Gif, ThemeBannerFormat.Webp },
        { ThemeBannerFormat.WebpAnimated, ThemeBannerFormat.Webp },
    };

    public static string GetBannerUrl(ISharedThemeMeta theme, ThemeBannerFormat format = ThemeBannerFormat.Webp)
    {
        if (!theme.HasCustomBanner)
            return "_content/Valour.Client/media/image-not-found.webp";

        if (!theme.HasCustomBanner)
        {
            if (AnimatedFormats.Contains(format))
            {
                format = AnimatedToStaticBackup[format];
            }
        }
        
        string formatStr = BannerFormatMap[format];
        return $"https://public-cdn.valour.gg/valour-public/themeBanners/{theme.Id}/{formatStr}";
    }
}