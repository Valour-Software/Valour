namespace Valour.Shared.Models.Themes;

public interface ISharedTheme : ISharedThemeMeta
{
    public const string DefaultFontFamily = "\"Outfit\", system-ui, -apple-system, BlinkMacSystemFont, \"Segoe UI\", sans-serif";
    public const int MaxFontFamilyLength = 256;
    public const int MaxRadiusLength = 16;

    public const string DefaultRadiusXs = "2px";
    public const string DefaultRadiusSm = "6px";
    public const string DefaultRadiusMd = "8px";
    public const string DefaultRadiusLg = "12px";
    public const string DefaultRadiusXl = "16px";
    public const string DefaultRadiusFull = "999px";

    public bool Published { get; set; }

    public string FontFamily { get; set; }
    public string RadiusXs { get; set; }
    public string RadiusSm { get; set; }
    public string RadiusMd { get; set; }
    public string RadiusLg { get; set; }
    public string RadiusXl { get; set; }
    public string RadiusFull { get; set; }
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

    public static string NormalizeFontFamily(string fontFamily)
    {
        if (string.IsNullOrWhiteSpace(fontFamily))
            return DefaultFontFamily;

        var trimmed = fontFamily.Trim();
        return IsFontFamilySafe(trimmed) ? trimmed : DefaultFontFamily;
    }

    public static bool IsFontFamilySafe(string fontFamily)
    {
        if (string.IsNullOrWhiteSpace(fontFamily) || fontFamily.Length > MaxFontFamilyLength)
            return false;

        var cssLower = fontFamily.ToLowerInvariant();
        if (cssLower.Contains("url(") || cssLower.Contains("@import") ||
            cssLower.Contains("@font-face") || cssLower.Contains("javascript:") ||
            cssLower.Contains("expression(") || cssLower.Contains("behavior:") ||
            cssLower.Contains("-moz-binding") || cssLower.Contains("</style"))
            return false;

        foreach (var c in fontFamily)
        {
            if (char.IsAsciiLetterOrDigit(c))
                continue;

            if (c is ' ' or '\t' or ',' or '"' or '\'' or '-' or '_' or '.')
                continue;

            return false;
        }

        return true;
    }

    public static string NormalizeRadius(string radius, string fallback)
    {
        if (string.IsNullOrWhiteSpace(radius))
            return fallback;

        var trimmed = radius.Trim();
        return IsRadiusSafe(trimmed) ? trimmed : fallback;
    }

    public static bool IsRadiusSafe(string radius)
    {
        if (string.IsNullOrWhiteSpace(radius) || radius.Length > MaxRadiusLength)
            return false;

        var valueEnd = 0;
        var hasDigit = false;
        var hasDot = false;
        var hasNonZeroDigit = false;

        while (valueEnd < radius.Length)
        {
            var c = radius[valueEnd];
            if (char.IsAsciiDigit(c))
            {
                hasDigit = true;
                hasNonZeroDigit |= c != '0';
                valueEnd++;
                continue;
            }

            if (c == '.' && !hasDot)
            {
                hasDot = true;
                valueEnd++;
                continue;
            }

            break;
        }

        if (!hasDigit)
            return false;

        var unit = radius[valueEnd..].ToLowerInvariant();
        if (unit.Length == 0)
            return !hasNonZeroDigit;

        return unit is "px" or "rem" or "em" or "%";
    }

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
        return $"{ValourHosts.PublicCdnBaseUrl}/valour-public/themeBanners/{theme.Id}/{formatStr}";
    }
}
