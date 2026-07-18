namespace Valour.Server.Utilities;

/// <summary>
/// Markdown-bypass protections shared by every user-authored markdown surface
/// (chat messages, threads, docs).
/// </summary>
public static class MarkdownProtections
{
    public static string Sanitize(string content)
    {
        if (string.IsNullOrEmpty(content))
            return content ?? string.Empty;

        content = content.Replace("[](", "[]\\(");
        content = content.Replace("]()", "]\\()");
        return content;
    }
}
