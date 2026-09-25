using System.Text.RegularExpressions;

namespace Valour.Shared.Models.Themes;

/// <summary>
/// Safety rules for user-authored theme CSS. Themes are shared publicly, so custom
/// CSS must not load remote resources (which would leak viewer IP addresses) or
/// select on attribute values. The server rejects unsafe CSS on save and the
/// client drops it before rendering.
/// </summary>
public static partial class ThemeCustomCss
{
    private static readonly string[] ResourceTokens =
    [
        // Functions that can fetch a resource from a URL or string.
        "url(",
        "src(",
        "image(",
        "image-set(",
        "cross-fade(",
        "element(",
        // At-rules that load or declare external resources.
        "@import",
        "@font-face",
        "@namespace",
    ];

    private static readonly string[] ScriptTokens =
    [
        "javascript:",
        "expression(",
        "behavior:",
        "-moz-binding",
        "</style",
    ];

    [GeneratedRegex(@"/\*.*?(\*/|$)", RegexOptions.Singleline)]
    private static partial Regex CommentRegex();

    /// <summary>
    /// Returns null when the CSS is allowed, or a user-facing reason it was rejected.
    /// </summary>
    public static string GetViolation(string css)
    {
        if (string.IsNullOrWhiteSpace(css))
            return null;

        // CSS escapes can spell any character (u\72l( is url(), @\69mport is @import),
        // so blocked names are only reliable when escapes are rejected outright.
        if (css.Contains('\\'))
            return "Backslashes (CSS escapes) are not allowed in custom CSS.";

        if (css.Contains('[') || css.Contains(']'))
            return "Attribute selectors [...] are not allowed.";

        // Check both the original text and a copy with comments removed, so a comment
        // placed inside a blocked name cannot hide it.
        var lower = css.ToLowerInvariant();
        var withoutComments = CommentRegex().Replace(lower, string.Empty);

        if (ContainsAny(lower, withoutComments, ResourceTokens))
            return "URLs, image functions, @import, @font-face, and @namespace are not allowed. " +
                   "Use theme asset variables directly, e.g. background-image: var(--theme-asset-name); (do not wrap with url()).";

        if (ContainsAny(lower, withoutComments, ScriptTokens))
            return "CSS contains disallowed content for security reasons.";

        return null;
    }

    private static bool ContainsAny(string lower, string withoutComments, string[] tokens)
    {
        foreach (var token in tokens)
        {
            if (lower.Contains(token) || withoutComments.Contains(token))
                return true;
        }

        return false;
    }

    public static bool IsSafe(string css) => GetViolation(css) is null;
}
