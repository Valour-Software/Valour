using Markdig.Syntax.Inlines;

namespace Valour.Client.Markdig;

/// <summary>
/// A custom planet emoji token in message markdown.
/// </summary>
public class ValourEmojiInline : LeafInline
{
    public string Match { get; set; }
    public long? CustomId { get; set; }
}