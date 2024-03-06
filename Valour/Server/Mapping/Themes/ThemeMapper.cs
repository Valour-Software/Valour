using Valour.Server.Models.Themes;

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
            ImageUrl = theme.ImageUrl,
            Published = theme.Published,
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
            ImageUrl = theme.ImageUrl,
            Published = theme.Published,
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