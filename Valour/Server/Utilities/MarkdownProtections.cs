using Valour.Shared.Utilities;

namespace Valour.Server.Utilities;

/// <summary>
/// Markdown-bypass protections for thread posts, comments, and wiki pages.
/// They apply the same rules as chat messages (see <see cref="MessageMarkdownSafety"/>).
/// </summary>
public static class MarkdownProtections
{
    public static string Sanitize(string content) =>
        MessageMarkdownSafety.Escape(content) ?? string.Empty;
}
