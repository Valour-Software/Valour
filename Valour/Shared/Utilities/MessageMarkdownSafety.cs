namespace Valour.Shared.Utilities;

/// <summary>
/// Neutralizes markdown that the message renderer would otherwise abuse.
/// The server applies this to plain-text messages. It cannot read encrypted
/// messages, so clients apply it after decrypting instead.
/// </summary>
public static class MessageMarkdownSafety
{
    /// <summary>
    /// Escapes empty link text and empty link targets. <c>[](url)</c> would
    /// load a direct image that is not proxied and reveals the reader's IP
    /// address, and <c>[text]()</c> hides text that moderators cannot reveal.
    /// A backslash is inserted rather than removing brackets, because removal
    /// could be defeated by nesting. Applying it twice has no further effect.
    /// </summary>
    public static string Escape(string content)
    {
        if (string.IsNullOrEmpty(content))
            return content;

        return content
            .Replace("[](", "[]\\(")
            .Replace("]()", "]\\()");
    }
}
