using System.Text;
using Markdig.Extensions.Emoji;
using Markdig.Helpers;
using Markdig.Parsers;
using Microsoft.Extensions.Primitives;

namespace Valour.Client.Markdig;

/// <summary>
/// The inline parser used for emojis.
/// </summary>
/// <seealso cref="InlineParser" />
public class ValourEmojiParser : InlineParser
{

    private static readonly HashSet<int> EmojiCodePoints = new HashSet<int>();
    
    /// <summary>
    /// Initializes a new instance of the <see cref="EmojiParser"/> class.
    /// </summary>
    public ValourEmojiParser()
    {
        List<char> openers = new List<char>(1024){ '«' };
        
        AddCachedRange(openers, 0x1F600, 0x1F64F); // Emoticons
        AddCachedRange(openers, 0x2600, 0x26FF); // Misc Symbols
        AddCachedRange(openers, 0x2700, 0x27BF); // Dingbats
        AddCachedRange(openers, 0x1F300, 0x1F5FF); // Other emojis
        AddCachedRange(openers, 0x1F900, 0x1F9FF); // Supplemental Symbols and Pictographs
        AddCachedRange(openers, 0x1F680, 0x1F6FF); // Transport and Map Symbols
        AddCachedRange(openers, 0x1F700, 0x1F77F); // Alchemical Symbols
        AddCachedRange(openers, 0x1FA70, 0x1FAFF); // Symbols and Pictographs Extended-A
        AddCachedRange(openers, 0x1F440, 0x1F4FC); // People & Body
        AddCachedRange(openers, 0x1F400, 0x1F43F); // Animals & Nature
        AddCachedRange(openers, 0x1F32D, 0x1F37F); // Food & Drink
        AddCachedRange(openers, 0x1F3A0, 0x1F3FF); // Activities

        for (int codePoint = 0xD800; codePoint <= 0xDBFF; codePoint++)
        {
            char highSurrogate = (char)codePoint;
            openers.Add(highSurrogate);
        }

        OpeningCharacters = openers.ToArray();
    }
    
    public static bool IsJoiner(char highSurrogate, char? lowSurrogate)
    {
        int codePoint;
        if (lowSurrogate is not null)
        {
            codePoint = Char.ConvertToUtf32(highSurrogate, lowSurrogate.Value);
        }
        else
        {
            codePoint = highSurrogate;
        }
    
        return codePoint == 0x200D; // Only ZWJ is considered a joiner
    }

    public static bool IsModifier(char highSurrogate, char? lowSurrogate)
    {
        int codePoint;
        if (lowSurrogate is not null)
        {
            codePoint = Char.ConvertToUtf32(highSurrogate, lowSurrogate.Value);
        }
        else
        {
            codePoint = highSurrogate;
        }

        return codePoint == 0xfe0f || (codePoint >= 0x1F3FB && codePoint <= 0x1F3FF); // Range for skin tone modifiers
    }
    
    public static void AddCachedRange(List<char> list, int start, int end)
    {
        for (var i = start; i <= end; i++)
        {
            EmojiCodePoints.Add(i);
            list.Add((char)i);
        }
    }

    enum EmojiState
    {
        Start,
        Emoji,
        Joiner,
        Modifier,
    }
    
    public static string GetEmoji(StringSlice input)
{
    if (input.Length == 0)
    {
        return null;
    }

    StringBuilder sb = null;
    EmojiState state = EmojiState.Start;

    for (int i = 0; i < input.Length; i++)
    {
        char currentChar = input.PeekChar(i);
        if (currentChar <= 127) // Quick check for ASCII
        {
            return sb?.ToString();
        }

        int codePoint = currentChar;
        if (char.IsHighSurrogate(currentChar))
        {
            char? nextChar = (i + 1 < input.Length) ? input.PeekChar(i + 1) : (char?)null;
            if (nextChar.HasValue && char.IsLowSurrogate(nextChar.Value))
            {
                codePoint = char.ConvertToUtf32(currentChar, nextChar.Value);
                i++; // Move past the low surrogate
            }
            else
            {
                return sb?.ToString(); // Invalid surrogate pair
            }
        }

        switch (state)
        {
            case EmojiState.Start:
            case EmojiState.Joiner:
                if (EmojiCodePoints.Contains(codePoint))
                {
                    sb ??= new StringBuilder();
                    sb.Append(currentChar);
                    if (char.IsLowSurrogate(input.PeekChar(i)))
                    {
                        sb.Append(input.PeekChar(i));
                    }
                    state = EmojiState.Emoji;
                }
                else
                {
                    return sb?.ToString(); // Not an emoji
                }
                break;

            case EmojiState.Emoji:
                if (IsJoiner(currentChar, char.IsLowSurrogate(input.PeekChar(i)) ? input.PeekChar(i) : (char?)null))
                {
                    sb?.Append(currentChar);
                    if (char.IsLowSurrogate(input.PeekChar(i)))
                    {
                        sb.Append(input.PeekChar(i));
                        i++;
                    }
                    state = EmojiState.Joiner;
                }
                else if (IsModifier(currentChar, char.IsLowSurrogate(input.PeekChar(i)) ? input.PeekChar(i) : (char?)null))
                {
                    sb?.Append(currentChar);
                    if (char.IsLowSurrogate(input.PeekChar(i)))
                    {
                        sb.Append(input.PeekChar(i));
                        i++;
                    }
                    state = EmojiState.Modifier;
                }
                else
                {
                    return sb?.ToString(); // End of emoji sequence
                }
                break;
        }
    }

    return sb?.ToString();
}

    public override bool Match(InlineProcessor processor, ref StringSlice slice)
    {
        // First: Native emoji parsing
        
        //Console.WriteLine("START");
        var native = GetEmoji(slice);
        //Console.WriteLine("STOP");
        if (native != null)
        {
            ValourEmojiInline emoji = new()
            {
                Native = EmojiHelpers.EmojiToUnified(native),
                CustomId = null,
            };
            
            processor.Inline = emoji;
            slice.Start += native.Length;
            return true;
        }
        
        // Format: «e-:smile:» for normal emojis
        //         «e-:custom:-id» for custom emojis (where x is the emoji id)

        if (slice.CurrentChar != '«' ||
            slice.PeekChar(1) != 'e' ||
            slice.PeekChar(2) != '-')
        {
            return false;
        }
        
        // Now we parse the textual value of the emoji
        // There can be multiple in a row for different variations of emojis,
        // for example :+1::skin-tone-1:
        if (slice.PeekChar(3) == ':')
        {
            // Scan the emoji text until the end. It has to end with a colon, and then
            // either a tilde if it has a custom id, or a closing angle bracket if it doesn't
            // This will also give up after 30 characters, which should be enough for any emoji.
            StringBuilder emojiBuilder = new(":");
            char currentChar = slice.PeekChar(4);
            for (int i = 0; i < 30; i++)
            {
                // Check for the end, which will either be a dash, or a closing angle bracket
                if (currentChar == '»')
                {
                    // Ensure the character before this was a colon
                    if (slice.PeekChar(i + 3) != ':')
                    {
                        return false;
                    }
                    
                    // We have a valid emoji!
                    // Now we need to add it to the processor
                    // We do this by adding a new emoji inline
                    // We also need to advance the slice by the length of the emoji
                    // We do this by adding the length of the emoji to the slice's start position
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
                    // Ensure the character before this was a colon
                    if (slice.PeekChar(i + 3) != ':')
                    {
                        return false;
                    }
                    
                    // We have a valid emoji!
                    // Now we have to scan for the custom id, which is a number between the dash and the closing angle bracket
                    StringBuilder idBuilder = new();
                    currentChar = slice.PeekChar(i + 5);
                    for (int j = 0; j < 20; j++)
                    {
                        if (currentChar == '»')
                        {
                            // We have a valid emoji!
                            // Now we need to add it to the processor
                            // We do this by adding a new emoji inline
                            // We also need to advance the slice by the length of the emoji
                            // We do this by adding the length of the emoji to the slice's start position
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
        
        return true;
    }
}