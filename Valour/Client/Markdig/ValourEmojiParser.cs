using Markdig.Extensions.Emoji;
using Markdig.Helpers;
using Markdig.Parsers;

namespace Valour.Client.Markdig;

/// <summary>
/// The inline parser used for emojis.
/// </summary>
/// <seealso cref="InlineParser" />
public class ValourEmojiParser : InlineParser
{
    private readonly ValourEmojiMapping _emojiMapping;

    /// <summary>
    /// Initializes a new instance of the <see cref="EmojiParser"/> class.
    /// </summary>
    public ValourEmojiParser(ValourEmojiMapping emojiMapping)
    {
        _emojiMapping = emojiMapping;
        OpeningCharacters = _emojiMapping.OpeningCharacters;
    }

    public override bool Match(InlineProcessor processor, ref StringSlice slice)
    {
        // Previous char must be a space
        //if (!slice.PeekCharExtra(-1).IsWhiteSpaceOrZero())
        //{
        //    return false;
        //}

        // Try to match an emoji shortcode or smiley
        if (!_emojiMapping.PrefixTree.TryMatchLongest(slice.Text.AsSpan(slice.Start, slice.Length), out KeyValuePair<string, string> match))
        {
            return false;
        }
        
        //Console.WriteLine(match.Key);
        //Console.WriteLine(match.Value);
        //Console.WriteLine(string.Concat(match.Value.Select(x => ((ushort)x).ToString("x"))));
        
        // Get the twemoji code
        var twemoji = EmojiHelpers.EmojiToTwemoji(match.Value);
        if (twemoji == null)
            return false;
        
        // Push the EmojiInline
        processor.Inline = new ValourEmojiInline()
        {
            Span =
            {
                Start = processor.GetSourcePosition(slice.Start, out int line, out int column),
            },
            Line = line,
            Column = column,
            Match = match.Value,
            Twemoji = twemoji
        };
        processor.Inline.Span.End = processor.Inline.Span.Start + match.Key.Length - 1;

        // Move the cursor to the character after the matched string
        slice.Start += match.Key.Length;

        return true;
    }
}