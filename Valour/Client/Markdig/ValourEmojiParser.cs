using System.Text;
using Markdig.Helpers;
using Markdig.Parsers;

namespace Valour.Client.Markdig;

/// <summary>
/// Parses custom planet emoji tokens, such as «e-:name~123:», into inline
/// images. Unicode emoji are left as text and drawn by the system font.
/// </summary>
/// <seealso cref="InlineParser" />
public class ValourEmojiParser : InlineParser
{
    public ValourEmojiParser()
    {
        OpeningCharacters = ['«'];
    }

    public override bool Match(InlineProcessor processor, ref StringSlice slice)
    {
        if (slice.CurrentChar != '«' ||
            slice.PeekChar(1) != 'e' ||
            slice.PeekChar(2) != '-')
        {
            return false;
        }

        if (slice.PeekChar(3) == ':')
        {
            StringBuilder emojiBuilder = new(":");
            char currentChar = slice.PeekChar(4);
            for (int i = 0; i < 40; i++)
            {
                if (currentChar == '»')
                {
                    if (slice.PeekChar(i + 3) != ':')
                    {
                        return false;
                    }

                    var emoji = new ValourEmojiInline
                    {
                        Match = emojiBuilder.ToString(),
                        CustomId = null,
                    };

                    processor.Inline = emoji;
                    slice.Start += i + 5;
                    return true;
                }
                else if (currentChar == '~')
                {
                    if (slice.PeekChar(i + 3) != ':')
                    {
                        return false;
                    }

                    StringBuilder idBuilder = new();
                    currentChar = slice.PeekChar(i + 5);
                    for (int j = 0; j < 20; j++)
                    {
                        if (currentChar == '»')
                        {
                            var emoji = new ValourEmojiInline
                            {
                                Match = emojiBuilder.ToString(),
                                CustomId = long.Parse(idBuilder.ToString()),
                            };

                            processor.Inline = emoji;
                            slice.Start += i + j + 6;
                            return true;
                        }
                        else if (char.IsDigit(currentChar))
                        {
                            idBuilder.Append(currentChar);
                            currentChar = slice.PeekChar(i + 6 + j);
                        }
                        else
                        {
                            return false;
                        }
                    }
                }

                emojiBuilder.Append(currentChar);
                currentChar = slice.PeekChar(i + 5);
            }
        }
        return false;
    }
}
