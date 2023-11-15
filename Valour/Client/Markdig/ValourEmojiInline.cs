using Markdig.Extensions.Emoji;
using Markdig.Helpers;
using Markdig.Syntax.Inlines;

namespace Valour.Client.Markdig;

public class ValourEmojiInline : LeafInline
{
    /// <summary>
    /// Initializes a new instance of the <see cref="EmojiInline"/> class.
    /// </summary>
    /// <param name="content">The content.</param>
    public ValourEmojiInline()
    {
    }

    public string Native { get; set; }
    public string Match { get; set; }
    public long? CustomId { get; set; }
}