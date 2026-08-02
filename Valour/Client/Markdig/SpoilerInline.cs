using Markdig.Syntax.Inlines;

namespace Valour.Client.Markdig;

/// <summary>
/// A ||spoiler|| span. Reuses Markdig's built-in delimiter-run/emphasis machinery
/// (via SpoilerExtension registering '|' as an emphasis character), so nested
/// inline content (bold, mentions, emoji, etc.) inside a spoiler parses normally.
/// </summary>
public class SpoilerInline : EmphasisInline
{
}
