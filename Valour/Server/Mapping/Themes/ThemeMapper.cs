using Valour.Server.Models.Themes;
using Valour.Shared.Models.Themes;

namespace Valour.Server.Mapping.Themes;

public static class ThemeMapper
{
    public static Theme ToModel(this Valour.Database.Themes.Theme theme)
    {
        if (theme is null)
            return null;
        
        return new Theme()
        {
            Id = theme.Id,
            AuthorId = theme.AuthorId,
            Name = theme.Name,
            Description = theme.Description,
            HasCustomBanner = theme.HasCustomBanner,
            HasAnimatedBanner = theme.HasAnimatedBanner,
            Published = theme.Published,
            FontFamily = ISharedTheme.NormalizeFontFamily(theme.FontFamily),
            RadiusXs = ISharedTheme.NormalizeRadius(theme.RadiusXs, ISharedTheme.DefaultRadiusXs),
            RadiusSm = ISharedTheme.NormalizeRadius(theme.RadiusSm, ISharedTheme.DefaultRadiusSm),
            RadiusMd = ISharedTheme.NormalizeRadius(theme.RadiusMd, ISharedTheme.DefaultRadiusMd),
            RadiusLg = ISharedTheme.NormalizeRadius(theme.RadiusLg, ISharedTheme.DefaultRadiusLg),
            RadiusXl = ISharedTheme.NormalizeRadius(theme.RadiusXl, ISharedTheme.DefaultRadiusXl),
            RadiusFull = ISharedTheme.NormalizeRadius(theme.RadiusFull, ISharedTheme.DefaultRadiusFull),
            FontColor = theme.FontColor,
            FontAltColor = theme.FontAltColor,
            LinkColor = theme.LinkColor,
            MainColor1 = theme.MainColor1,
            MainColor2 = theme.MainColor2,
            MainColor3 = theme.MainColor3,
            MainColor4 = theme.MainColor4,
            MainColor5 = theme.MainColor5,
            TintColor = theme.TintColor,
            VibrantPurple = theme.VibrantPurple,
            VibrantBlue = theme.VibrantBlue,
            VibrantCyan = theme.VibrantCyan,
            PastelCyan = theme.PastelCyan,
            PastelCyanPurple = theme.PastelCyanPurple,
            PastelPurple = theme.PastelPurple,
            PastelRed = theme.PastelRed,
            CustomCss = theme.CustomCss
        };
    }
    
    public static Valour.Database.Themes.Theme ToDatabase(this Theme theme)
    {
        if (theme is null)
            return null;
        
        return new Valour.Database.Themes.Theme()
        {
            Id = theme.Id,
            AuthorId = theme.AuthorId,
            Name = theme.Name,
            Description = theme.Description,
            HasCustomBanner = theme.HasCustomBanner,
            HasAnimatedBanner = theme.HasAnimatedBanner,
            Published = theme.Published,
            FontFamily = ISharedTheme.NormalizeFontFamily(theme.FontFamily),
            RadiusXs = ISharedTheme.NormalizeRadius(theme.RadiusXs, ISharedTheme.DefaultRadiusXs),
            RadiusSm = ISharedTheme.NormalizeRadius(theme.RadiusSm, ISharedTheme.DefaultRadiusSm),
            RadiusMd = ISharedTheme.NormalizeRadius(theme.RadiusMd, ISharedTheme.DefaultRadiusMd),
            RadiusLg = ISharedTheme.NormalizeRadius(theme.RadiusLg, ISharedTheme.DefaultRadiusLg),
            RadiusXl = ISharedTheme.NormalizeRadius(theme.RadiusXl, ISharedTheme.DefaultRadiusXl),
            RadiusFull = ISharedTheme.NormalizeRadius(theme.RadiusFull, ISharedTheme.DefaultRadiusFull),
            FontColor = theme.FontColor,
            FontAltColor = theme.FontAltColor,
            LinkColor = theme.LinkColor,
            MainColor1 = theme.MainColor1,
            MainColor2 = theme.MainColor2,
            MainColor3 = theme.MainColor3,
            MainColor4 = theme.MainColor4,
            MainColor5 = theme.MainColor5,
            TintColor = theme.TintColor,
            VibrantPurple = theme.VibrantPurple,
            VibrantBlue = theme.VibrantBlue,
            VibrantCyan = theme.VibrantCyan,
            PastelCyan = theme.PastelCyan,
            PastelCyanPurple = theme.PastelCyanPurple,
            PastelPurple = theme.PastelPurple,
            PastelRed = theme.PastelRed,
            CustomCss = theme.CustomCss
        };
    }
}
