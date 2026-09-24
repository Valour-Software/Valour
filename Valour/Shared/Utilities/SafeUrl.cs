namespace Valour.Shared.Utilities;

/// <summary>
/// Scheme allowlisting for any URL that ends up in an href, src, or a
/// window.open call. Markdown disables raw HTML everywhere, so link URLs are
/// the remaining script-injection vector: [x](javascript:...) survives HTML
/// escaping and runs on click.
/// </summary>
public static class SafeUrl
{
    /// <summary>
    /// Substitute for a URL that fails the allowlist. Keeps the link visible
    /// but inert rather than silently dropping the author's text.
    /// </summary>
    public const string Unsafe = "#";

    private static readonly string[] AllowedSchemes = ["http", "https", "mailto"];

    /// <summary>
    /// Whether the URL is safe to place in an href/src.
    /// Relative URLs (no scheme) are allowed - they cannot carry a scheme, and
    /// internal wiki/app links depend on them.
    /// </summary>
    public static bool IsSafe(string? url)
    {
        if (string.IsNullOrWhiteSpace(url))
            return false;

        var scheme = ExtractScheme(url);

        // No scheme means a relative URL, which cannot execute script.
        if (scheme is null)
            return true;

        foreach (var allowed in AllowedSchemes)
        {
            if (scheme.Equals(allowed, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    /// <summary>
    /// Whether the URL is a path on the current origin, safe to use as a
    /// post-login redirect. It must start with a single "/"; "//host" and
    /// "/\host" are treated by browsers as links to another host, and
    /// whitespace or control characters are rejected because browsers strip
    /// them before parsing ("/\t/host" becomes "//host").
    /// </summary>
    public static bool IsLocalPath(string? url)
    {
        if (string.IsNullOrEmpty(url) || url[0] != '/')
            return false;

        if (url.Length > 1 && url[1] is '/' or '\\')
            return false;

        foreach (var c in url)
        {
            if (c <= 0x20 || c == 0x7F || c == '\\')
                return false;
        }

        return true;
    }

    /// <summary>
    /// Returns the URL if it passes the allowlist, otherwise an inert placeholder.
    /// </summary>
    public static string Sanitize(string? url) =>
        IsSafe(url) ? url! : Unsafe;

    /// <summary>
    /// Extracts the URL scheme the way a browser would, or null if the URL is
    /// relative. Browsers ignore ASCII whitespace and control characters while
    /// looking for the scheme delimiter, so "java\tscript:x" and " javascript:x"
    /// both resolve to the javascript scheme - we must strip them before
    /// comparing or the allowlist is trivially bypassed.
    /// </summary>
    private static string? ExtractScheme(string url)
    {
        Span<char> buffer = stackalloc char[url.Length];
        var length = 0;

        foreach (var c in url)
        {
            // Control chars and whitespace are dropped, matching browser parsing.
            if (c <= 0x20 || c == 0x7F)
                continue;

            // The scheme ends at the first colon; a slash, query, fragment, or
            // backslash first means there was no scheme at all.
            if (c == ':')
                return length == 0 ? null : IsValidScheme(buffer[..length]) ? new string(buffer[..length]) : null;

            if (c is '/' or '?' or '#' or '\\')
                return null;

            buffer[length++] = c;
        }

        return null;
    }

    /// <summary>
    /// A scheme is ALPHA *( ALPHA / DIGIT / "+" / "-" / "." ) per RFC 3986.
    /// Anything else is not a scheme, so the URL is relative.
    /// </summary>
    private static bool IsValidScheme(ReadOnlySpan<char> scheme)
    {
        if (!char.IsAsciiLetter(scheme[0]))
            return false;

        foreach (var c in scheme)
        {
            if (!char.IsAsciiLetterOrDigit(c) && c is not ('+' or '-' or '.'))
                return false;
        }

        return true;
    }
}
