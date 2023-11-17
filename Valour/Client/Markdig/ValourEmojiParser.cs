using System.Text;
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

    /// <summary>
    /// The lookup table for if a unicode codepoint is an emoji
    /// </summary>
    private static readonly HashSet<int> EmojiCodePoints = new HashSet<int>();
    
    /// <summary>
    /// Initializes a new instance of the <see cref="EmojiParser"/> class.
    /// </summary>
    public ValourEmojiParser()
    {
        var openers = new List<char>(1024){ '«' };
        
        // These are all in the BMP and can be detected with a single char
        // The BMP ends at 0xFFFF, so we don't need to check for anything higher
        AddCachedRange(openers, 0x1F600, 0x1F64F, true); // Emoticons
        AddCachedRange(openers, 0x2600, 0x26FF, true); // Misc Symbols
        AddCachedRange(openers, 0x2700, 0x27BF, true); // Dingbats
        AddCachedRange(openers, 0x1F300, 0x1F5FF, true); // Other emojis
        
        // These are all outside of the BMP and are covered by high surrogate detection
        
        AddCachedRange(openers, 0x1F900, 0x1F9FF); // Supplemental Symbols and Pictographs
        AddCachedRange(openers, 0x1F680, 0x1F6FF); // Transport and Map Symbols
        AddCachedRange(openers, 0x1F700, 0x1F77F); // Alchemical Symbols
        AddCachedRange(openers, 0x1FA70, 0x1FAFF); // Symbols and Pictographs Extended-A
        AddCachedRange(openers, 0x1F440, 0x1F4FC); // People & Body
        AddCachedRange(openers, 0x1F400, 0x1F43F); // Animals & Nature
        AddCachedRange(openers, 0x1F32D, 0x1F37F); // Food & Drink
        AddCachedRange(openers, 0x1F3A0, 0x1F3FF); // Activities

        // Cover all of the high surrogates
        for (int codePoint = 0xD800; codePoint <= 0xDBFF; codePoint++)
        {
            char highSurrogate = (char)codePoint;
            openers.Add(highSurrogate);
        }

        // Set array of opening character
        OpeningCharacters = openers.ToArray();
    }
    
    /// <summary>
    /// Returns if a character is an emoji joiner.
    /// </summary>
    /// <param name="highSurrogate">The first UTF-16 character if a surrogate pair, otherwise the target char</param>
    /// <param name="lowSurrogate">The second UTF-16 character if a surrogate pair. Null otherwise.</param>
    /// <returns>The codepoint if this is a joiner, otherwise returns 0</returns>
    private static int GetJoiner(char highSurrogate, char? lowSurrogate)
    {
        // Merge the surrogate pair if necessary
        var codePoint = lowSurrogate is not null ? 
            char.ConvertToUtf32(highSurrogate, lowSurrogate.Value) : 
            highSurrogate;
        
        return codePoint == 0x200D ? 
            codePoint : 0; // Only ZWJ is considered a joiner
    }

    /// <summary>
    /// Returns if a character is an emoji modifier.
    /// </summary>
    /// <param name="highSurrogate">The first UTF-16 character if a surrogate pair, otherwise the target char</param>
    /// <param name="lowSurrogate">The second UTF-16 character if a surrogate pair. Null otherwise.</param>
    /// <returns>The codepoint if this is a modifier, otherwise returns 0</returns>
    private static int GetModifier(char highSurrogate, char? lowSurrogate)
    {
        // Merge the surrogate pair if necessary
        var codePoint = lowSurrogate is not null ? 
            char.ConvertToUtf32(highSurrogate, lowSurrogate.Value) : 
            highSurrogate;

        return codePoint == 0xfe0f || (codePoint >= 0x1F3FB && codePoint <= 0x1F3FF) ? 
                codePoint : 0; // Range for skin tone modifiers
    }
    
    /// <summary>
    /// Adds a range of code points to the emoji lookup table. Optionally adds them to the
    /// opener list, which is used for starting the parsing process.
    /// </summary>
    /// <param name="list">The opener list</param>
    /// <param name="start">The start of the range to add</param>
    /// <param name="end">The end of the range to add</param>
    /// <param name="opener">True if the range should be added as opening characters</param>
    public static void AddCachedRange(List<char> list, int start, int end, bool opener = false)
    {
        for (var i = start; i <= end; i++)
        {
            // Add to the lookup table
            EmojiCodePoints.Add(i);

            // Add to the opener list if necessary
            if (opener)
            {
                list.Add((char)i);
            }
        }
    }

    /// <summary>
    /// Used to track the state of the parser
    /// </summary>
    enum EmojiState
    {
        // Optimization:
        // We can use a bitmask to track the state of the parser
        // Start and Joiner both have the last bit set to 1
        // This reduces a branch on each iteration
        Start =    0b0001,
        Emoji =    0b0010,
        Joiner =   0b0011,
        Modifier = 0b0100,
    }
    
    // TODO: This is an optimization that also makes multithreading
    // impossible. But Blazor doesn't support true multithreading anyway,
    // so it's not a big deal... yet.
    private static int _charCount = 0;
    private static readonly List<int> CodePoints = new();
    
    private static void ParseSliceEmojis(StringSlice input)
    {
        // Clear last slice's data
        _charCount = 0;
        CodePoints.Clear();
        
        // Cancel if the slice is empty
        if (input.Length == 0)
        {
            return;
        }

        //int s = 0;
        //while (input.PeekChar(s) != '\0')
        //{
        //    Console.WriteLine("0x{0:x}", Convert.ToInt32(input.PeekChar(s)));
        //    Console.WriteLine(input.PeekChar(s));
        //    s++;
        //}

        // Start the parser with the special 'start' state
        var state = EmojiState.Start;

        // Loop through the remainder of the slice
        for (var i = 0; i < input.Length; i++)
        {
            //Console.WriteLine("State: " + state.ToString());
            
            // Get the character at the current position
            var currentChar = input.PeekChar(i);
            
            //Console.WriteLine("Current char: {0} (0x{1:x})", currentChar, Convert.ToInt32(currentChar));
            
            // States where the next character should be an emoji
            // This is equivalent to IF state == Start OR state == Joiner
            if (((int)state & 0b01) == 0b01)
            {
                // Early exit for ASCII characters
                if (currentChar <= 127)
                {
                    // Finished
                    return;
                }

                // Prepare low surrogate variable 
                char? lowSurrogate = null;

                int codePoint;
                if (char.IsHighSurrogate(currentChar))
                {
                    //Console.WriteLine("High Surrogate!");
                    
                    lowSurrogate = input.PeekChar(i + 1);
                    if (char.IsLowSurrogate(lowSurrogate.Value))
                    {
                        //Console.WriteLine("Low Surrogate!");
                        codePoint = char.ConvertToUtf32(currentChar, lowSurrogate.Value);
                    }
                    else
                    {
                        //Console.WriteLine("STOP: Invalid surrogate pair");
                        return;
                    }
                }
                else
                {
                    codePoint = currentChar;
                }
                
                //Console.WriteLine("Codepoint: 0x{0:x}", codePoint);

                // Add valid emoji characters to the string builder
                if (EmojiCodePoints.Contains(codePoint))
                {
                    CodePoints.Add(codePoint);
                    _charCount++;
                    
                    if (lowSurrogate is not null)
                    {
                        _charCount++;
                        // Advance the slice by one more character
                        i++;
                    }

                    state = EmojiState.Emoji;
                    //Console.WriteLine("Emoji: " + sb.ToString());
                }
                else
                {
                    // Finished (not an emoji)
                    //Console.WriteLine("STOP: Not an emoji");
                    return;
                }
            }
            else if (state == EmojiState.Emoji)
            {
                char? lowSurrogate = null;
                if (char.IsHighSurrogate(currentChar))
                {
                    //Console.WriteLine("High Surrogate! (Modifier/Joiner)");
                    var next = input.PeekChar(i + 1);
                    if (char.IsLowSurrogate(next))
                    {
                        lowSurrogate = next;
                    }
                    else
                    {
                        //Console.WriteLine("STOP: Invalid surrogate pair");
                        return;
                    }
                }

                int j = GetJoiner(currentChar, lowSurrogate);
                // Only thing allowed after emoji is joiner or modifier
                if (j != 0)
                {
                    state = EmojiState.Joiner;
                    CodePoints.Add(j);
                    _charCount++;
                    
                    if (lowSurrogate is not null)
                    {
                        _charCount++;
                        // Advance the slice by one more character
                        i++;
                    }
                    
                    continue;
                }
                
                var m = GetModifier(currentChar, lowSurrogate);
                if (m != 0)
                {
                    state = EmojiState.Modifier;
                    CodePoints.Add(m);
                    _charCount++;
                    
                    // Add modifier to the string builder
                    if (lowSurrogate is not null)
                    {
                        _charCount++;
                        // Advance the slice by one more character
                        i++;
                    }

                    //Console.WriteLine("Modifier!");
                }
                else
                {
                    // Finished (invalid character)
                    //Console.WriteLine("STOP: Invalid character");
                    return;
                }
            }
            else
            {
                //Console.WriteLine("STOP: Invalid state");
                return;
            }
        }

        return;
    }

    StringBuilder _nativeBuilder = new StringBuilder();
    public override bool Match(InlineProcessor processor, ref StringSlice slice)
    {
        // First: Native emoji parsing
        
        //Console.WriteLine("START");
        ParseSliceEmojis(slice);
        //Console.WriteLine("STOP");
        if (CodePoints.Count > 0)
        {
            _nativeBuilder.Clear();
            
            for (int i = 0; i < CodePoints.Count; i++)
            {
                _nativeBuilder.AppendFormat("{0:x}", CodePoints[i]);
                if (i != CodePoints.Count - 1)
                    _nativeBuilder.Append('-');
            }
            
            ValourEmojiInline emoji = new()
            {
                Native = _nativeBuilder.ToString(),
                CustomId = null,
            };
            
            processor.Inline = emoji;
            slice.Start += _charCount;
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