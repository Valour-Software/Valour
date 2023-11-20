using Markdig.Extensions.Emoji;
using Markdig.Syntax.Inlines;

namespace Valour.Client.Markdig;

/// <summary>
/// Represents a native emoji inline.
/// </summary>
public class NativeEmojiInline : LeafInline
{
    /// <summary>
    /// Initializes a new instance of the <see cref="NativeEmojiInline"/> class.
    /// </summary>
    /// <param name="unified">The unified representation of the emoji.</param>
    public NativeEmojiInline(string unified)
    {
        Unified = unified;
    }
    
    /// <summary>
    /// The unified representation of the emoji.
    /// ie "xxxxx-xxxxx-xxxxx" 
    /// </summary>
    public string Unified { get; set; }
}