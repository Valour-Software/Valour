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
    
    public string Twemoji { get; set; }

    /// <summary>
    /// Gets or sets the original match string (either an emoji shortcode or a text smiley)
    /// </summary>
    public string Match { get; set; }
}