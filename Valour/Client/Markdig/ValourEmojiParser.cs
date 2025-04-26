using System.Diagnostics;
using System.Text;
using Markdig.Extensions.Emoji;
using Markdig.Helpers;
using Markdig.Parsers;
using Valour.Client.Emojis;

namespace Valour.Client.Markdig;

/// <summary>
/// The inline parser used for emojis.
/// </summary>
/// <seealso cref="InlineParser" />
public class ValourEmojiParser : InlineParser
{
    public ValourEmojiParser()
    {
        var openers = new List<char>(1024);
        for (int codePoint = 0xD800; codePoint <= 0xDBFF; codePoint++)
            openers.Add((char)codePoint);
        for (int i = 0x2600; i <= 0x26FF; i++) openers.Add((char)i);
        for (int i = 0x2700; i <= 0x27BF; i++) openers.Add((char)i);
        for (int i = 0x1F300; i <= 0x1F5FF; i++) if (i <= 0xFFFF) openers.Add((char)i);
        for (int i = 0x1F600; i <= 0x1F64F; i++) if (i <= 0xFFFF) openers.Add((char)i);
        OpeningCharacters = openers.ToArray();
    }

    private CodePointLineBuffer _buffer;

    public override bool Match(InlineProcessor processor, ref StringSlice slice)
    {
        // var swTotal = Stopwatch.StartNew();

        // --- Profiling: Buffer creation ---
        // var swBuffer = Stopwatch.StartNew();
        if (_buffer == null || _buffer.Text != slice.Text)
        {
            _buffer = new CodePointLineBuffer(slice.Text);
        }
        // swBuffer.Stop();

        // --- Profiling: Code point index lookup ---
        // var swCpIndex = Stopwatch.StartNew();
        int cpIndex = Array.BinarySearch(_buffer.CodePointToCharIndex, 0, _buffer.CodePointCount, slice.Start);
        if (cpIndex < 0)
        {
            cpIndex = ~cpIndex;
        }
        // swCpIndex.Stop();

        // --- Profiling: Trie match ---
        // var swTrie = Stopwatch.StartNew();
        int maxLen = Math.Min(8, _buffer.CodePointCount - cpIndex);
        int emojiLength = MatchEmojiAt(
            new ReadOnlySpan<int>(_buffer.CodePoints, cpIndex, maxLen),
            EmojiTrieBuilder.RootNode);
        // swTrie.Stop();

        if (emojiLength > 0)
        {
            // --- Profiling: Emoji string build ---
            //var swBuild = Stopwatch.StartNew();
            var sb = new StringBuilder();
            for (int i = 0; i < emojiLength; i++)
            {
                sb.AppendFormat("{0:x}", _buffer.CodePoints[cpIndex + i]);
                if (i != emojiLength - 1)
                    sb.Append('-');
            }

            ValourEmojiInline emoji = new()
            {
                Native = sb.ToString(),
                CustomId = null,
            };

            processor.Inline = emoji;

            int charStart = _buffer.CodePointToCharIndex[cpIndex];
            int charEnd = (cpIndex + emojiLength < _buffer.CodePointCount)
                ? _buffer.CodePointToCharIndex[cpIndex + emojiLength]
                : _buffer.Text.Length;
            slice.Start = charEnd;
            //swBuild.Stop();

            // swTotal.Stop();
            //LogTimings(swTotal.Elapsed, swBuffer.Elapsed, swCpIndex.Elapsed, swTrie.Elapsed, swBuild.Elapsed, "NativeEmoji");

            return true;
        }

        // --- Profiling: Custom emoji logic ---
        // var swCustom = Stopwatch.StartNew();
        if (slice.CurrentChar != '«' ||
            slice.PeekChar(1) != 'e' ||
            slice.PeekChar(2) != '-')
        {
            // swCustom.Stop();
            // swTotal.Stop();
            //LogTimings(swTotal.Elapsed, swBuffer.Elapsed, swCpIndex.Elapsed, swTrie.Elapsed, TimeSpan.Zero, "CustomEmoji (fail fast)");
            return false;
        }

        if (slice.PeekChar(3) == ':')
        {
            StringBuilder emojiBuilder = new(":");
            char currentChar = slice.PeekChar(4);
            for (int i = 0; i < 30; i++)
            {
                if (currentChar == '»')
                {
                    if (slice.PeekChar(i + 3) != ':')
                    {
                        // swCustom.Stop();
                        // swTotal.Stop();
                        //LogTimings(swTotal.Elapsed, swBuffer.Elapsed, swCpIndex.Elapsed, swTrie.Elapsed, swCustom.Elapsed, "CustomEmoji (fail)");
                        return false;
                    }

                    var emoji = new ValourEmojiInline
                    {
                        Match = emojiBuilder.ToString(),
                        CustomId = null,
                    };

                    processor.Inline = emoji;
                    slice.Start += i + 5;
                    // swCustom.Stop();
                    // swTotal.Stop();
                    //LogTimings(swTotal.Elapsed, swBuffer.Elapsed, swCpIndex.Elapsed, swTrie.Elapsed, swCustom.Elapsed, "CustomEmoji (success)");
                    return true;
                }
                else if (currentChar == '~')
                {
                    if (slice.PeekChar(i + 3) != ':')
                    {
                        // swCustom.Stop();
                        // swTotal.Stop();
                        //LogTimings(swTotal.Elapsed, swBuffer.Elapsed, swCpIndex.Elapsed, swTrie.Elapsed, swCustom.Elapsed, "CustomEmoji (fail)");
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
                            // swCustom.Stop();
                            // swTotal.Stop();
                            //LogTimings(swTotal.Elapsed, swBuffer.Elapsed, swCpIndex.Elapsed, swTrie.Elapsed, swCustom.Elapsed, "CustomEmoji (success)");
                            return true;
                        }
                        else if (char.IsDigit(currentChar))
                        {
                            idBuilder.Append(currentChar);
                            currentChar = slice.PeekChar(i + 6 + j);
                        }
                        else
                        {
                            // swCustom.Stop();
                            // swTotal.Stop();
                            //LogTimings(swTotal.Elapsed, swBuffer.Elapsed, swCpIndex.Elapsed, swTrie.Elapsed, swCustom.Elapsed, "CustomEmoji (fail)");
                            return false;
                        }
                    }
                }

                emojiBuilder.Append(currentChar);
                currentChar = slice.PeekChar(i + 5);
            }
        }
        // swCustom.Stop();
        // swTotal.Stop();
        //LogTimings(swTotal.Elapsed, swBuffer.Elapsed, swCpIndex.Elapsed, swTrie.Elapsed, swCustom.Elapsed, "CustomEmoji (end)");
        return false;
    }

    public static int MatchEmojiAt(ReadOnlySpan<int> codePoints, EmojiTrieNode root)
    {
        var node = root;
        int lastMatch = -1;
        int i = 0;
        for (; i < codePoints.Length; i++)
        {
            if (!node.Children.TryGetValue(codePoints[i], out node))
                break;
            if (node.IsEmoji)
                lastMatch = i;
        }
        return lastMatch >= 0 ? lastMatch + 1 : 0;
    }

    private static void LogTimings(
        TimeSpan total,
        TimeSpan buffer,
        TimeSpan cpIndex,
        TimeSpan trie,
        TimeSpan buildOrCustom,
        string label)
    {
        Console.WriteLine(
            $"[{label}] Total: {total.TotalMilliseconds:F2} ms | " +
            $"Buffer: {buffer.TotalMilliseconds:F2} ms | " +
            $"CpIndex: {cpIndex.TotalMilliseconds:F2} ms | " +
            $"Trie: {trie.TotalMilliseconds:F2} ms | " +
            $"Build/Custom: {buildOrCustom.TotalMilliseconds:F2} ms");
    }
}
