namespace Valour.Client.Utility;

public readonly record struct TranslateLanguage(string Code, string Name);

/// <summary>
/// The languages selectable as the target for message translation, and the Google
/// translate endpoint's codes for them.
/// </summary>
public static class TranslateLanguages
{
    public static readonly IReadOnlyList<TranslateLanguage> All = new List<TranslateLanguage>
    {
        new("en", "English"),
        new("es", "Spanish"),
        new("fr", "French"),
        new("de", "German"),
        new("it", "Italian"),
        new("pt", "Portuguese"),
        new("nl", "Dutch"),
        new("ru", "Russian"),
        new("uk", "Ukrainian"),
        new("pl", "Polish"),
        new("sv", "Swedish"),
        new("no", "Norwegian"),
        new("da", "Danish"),
        new("fi", "Finnish"),
        new("el", "Greek"),
        new("tr", "Turkish"),
        new("ar", "Arabic"),
        new("he", "Hebrew"),
        new("hi", "Hindi"),
        new("bn", "Bengali"),
        new("th", "Thai"),
        new("vi", "Vietnamese"),
        new("id", "Indonesian"),
        new("ms", "Malay"),
        new("zh-CN", "Chinese (Simplified)"),
        new("zh-TW", "Chinese (Traditional)"),
        new("ja", "Japanese"),
        new("ko", "Korean"),
        new("cs", "Czech"),
        new("ro", "Romanian"),
        new("hu", "Hungarian"),
        new("sk", "Slovak"),
        new("bg", "Bulgarian"),
        new("hr", "Croatian"),
        new("sr", "Serbian"),
    };

    public static string GetName(string code)
    {
        foreach (var lang in All)
        {
            if (string.Equals(lang.Code, code, StringComparison.OrdinalIgnoreCase))
                return lang.Name;
        }

        return code;
    }
}
