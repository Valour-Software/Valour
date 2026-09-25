using System.Buffers.Binary;
using System.Text;
using Org.BouncyCastle.Crypto.Digests;
using Org.BouncyCastle.Crypto.Macs;
using Org.BouncyCastle.Crypto.Parameters;

namespace Valour.Sdk.E2ee;

/// <summary>
/// Keyed search terms for encrypted messages.
///
/// A client folds message text into tokens and computes a keyed hash for each
/// word, word prefix, word suffix, and leading command. The server stores
/// the hashes and matches them for search and automod, but cannot read or
/// guess the words because it does not have the channel's index key. It does
/// learn which messages in a channel share a term.
///
/// Folding uses fixed tables rather than culture or ICU behavior. Browsers run
/// with invariant globalization and native apps do not, and every client
/// must compute the same terms for the same text.
/// </summary>
public static class SearchTerms
{
    public const int MaxTerms = 400;
    public const int MaxAffixLength = 12;

    public const char Word = 'W';
    public const char Prefix = 'P';
    public const char Suffix = 'S';
    public const char Command = 'C';

    private static readonly HashSet<string> StopWords = new(StringComparer.Ordinal)
    {
        "a", "an", "and", "are", "as", "at", "be", "but", "by", "for", "if", "in", "into", "is", "it",
        "of", "on", "or", "so", "such", "that", "the", "their", "then", "there", "these", "they", "this",
        "to", "was", "will", "with"
    };

    /// <summary>
    /// Computes the terms uploaded with a message.
    /// </summary>
    public static int[] ForMessage(byte[] indexKey, long channelId, string content)
    {
        // The terms are the leading command, then every word, then every
        // prefix, then every suffix, cut off after the first MaxTerms entries
        // (repeats included). Whole words matter most for search and automod,
        // so they are kept first when a long message exceeds the limit.
        // Terms past the limit are never computed, and repeated tokens are
        // hashed once, because every recipient recomputes these terms to check
        // a message and keyed hashing is slow in interpreted WebAssembly.
        var tokens = Tokenize(content).Where(t => t.IsCjk || !StopWords.Contains(t.Text)).ToList();
        var hasher = new TermHasher(indexKey, channelId);
        var terms = new List<int>(MaxTerms);

        var command = LeadingCommand(content);
        if (command is not null)
            terms.Add(hasher.Term(Command, command));

        foreach (var token in tokens)
        {
            if (terms.Count >= MaxTerms)
                return Normalize(terms);
            terms.Add(hasher.Term(Word, token.Text));
        }

        foreach (var token in tokens)
        {
            if (token.IsCjk)
                continue;
            for (var length = 2; length <= Math.Min(token.Text.Length, MaxAffixLength); length++)
            {
                if (terms.Count >= MaxTerms)
                    return Normalize(terms);
                terms.Add(hasher.Term(Prefix, token.Text[..length]));
            }
        }

        foreach (var token in tokens)
        {
            if (token.IsCjk)
                continue;
            for (var length = 3; length <= Math.Min(token.Text.Length, MaxAffixLength); length++)
            {
                if (terms.Count >= MaxTerms)
                    return Normalize(terms);
                terms.Add(hasher.Term(Suffix, token.Text[^length..]));
            }
        }

        return Normalize(terms);
    }

    /// <summary>
    /// Computes terms for one index key and channel. It keeps one keyed hash
    /// whose key schedule is computed once, and remembers the terms it
    /// already computed, so repeated words and affixes cost nothing.
    /// </summary>
    private sealed class TermHasher
    {
        private readonly HMac _mac;
        private readonly Dictionary<(char Kind, string Token), int> _computed = new();
        private readonly byte[] _output;
        private byte[] _buffer = new byte[64];
        private readonly long _channelId;

        public TermHasher(byte[] indexKey, long channelId)
        {
            _mac = new HMac(new Sha256Digest());
            _mac.Init(new KeyParameter(indexKey));
            _output = new byte[_mac.GetMacSize()];
            _channelId = channelId;
        }

        public int Term(char kind, string token)
        {
            if (_computed.TryGetValue((kind, token), out var cached))
                return cached;

            // Same bytes as E2eeWriter: magic, channel, kind, then the
            // length-prefixed UTF-8 token.
            var tokenLength = Encoding.UTF8.GetByteCount(token);
            var length = 17 + tokenLength;
            if (_buffer.Length < length)
                _buffer = new byte[length];
            "VST1"u8.CopyTo(_buffer);
            BinaryPrimitives.WriteInt64BigEndian(_buffer.AsSpan(4), _channelId);
            _buffer[12] = (byte)kind;
            BinaryPrimitives.WriteInt32BigEndian(_buffer.AsSpan(13), tokenLength);
            Encoding.UTF8.GetBytes(token, _buffer.AsSpan(17));

            // DoFinal resets the hash to its keyed starting state.
            _mac.BlockUpdate(_buffer, 0, length);
            _mac.DoFinal(_output, 0);
            var term = BinaryPrimitives.ReadInt32BigEndian(_output);
            _computed[(kind, token)] = term;
            return term;
        }
    }

    /// <summary>
    /// Terms a search query must all match. Earlier words must match whole
    /// words; the last word may be the start of a word.
    /// </summary>
    public static int[] ForQuery(byte[] indexKey, long channelId, string query)
    {
        var tokens = Tokenize(query).Where(t => t.IsCjk || !StopWords.Contains(t.Text)).ToList();
        var terms = new List<int>();
        for (var i = 0; i < tokens.Count; i++)
        {
            var token = tokens[i];
            var isLast = i == tokens.Count - 1;
            if (isLast && !token.IsCjk && token.Text.Length >= 2 && token.Text.Length <= MaxAffixLength)
                terms.Add(Term(indexKey, channelId, Prefix, token.Text));
            else
                terms.Add(Term(indexKey, channelId, Word, token.Text));
        }
        return Normalize(terms);
    }

    /// <summary>
    /// The alternatives that make an automod word trigger fire. Each
    /// alternative is a set of terms that must all be present. A single word
    /// matches as a whole word, the start of a word, or the end of a word; a
    /// phrase matches when all of its words are present.
    /// </summary>
    public static List<int[]> ForTriggerWord(byte[] indexKey, long channelId, string triggerWord)
    {
        // Messages carry no terms for stop words, so a phrase is matched on
        // its other words. A trigger made only of stop words cannot match.
        var tokens = Tokenize(triggerWord).Where(t => t.IsCjk || !StopWords.Contains(t.Text)).ToList();
        var alternatives = new List<int[]>();
        if (tokens.Count == 0)
            return alternatives;

        if (tokens.Count > 1)
        {
            alternatives.Add(Normalize(tokens.Select(t => Term(indexKey, channelId, Word, t.Text))));
            return alternatives;
        }

        var text = tokens[0].Text;
        alternatives.Add([Term(indexKey, channelId, Word, text)]);
        if (!tokens[0].IsCjk && text.Length <= MaxAffixLength)
        {
            if (text.Length >= 2)
                alternatives.Add([Term(indexKey, channelId, Prefix, text)]);
            if (text.Length >= 3)
                alternatives.Add([Term(indexKey, channelId, Suffix, text)]);
        }
        return alternatives;
    }

    public static int ForTriggerCommand(byte[] indexKey, long channelId, string command) =>
        Term(indexKey, channelId, Command, "/" + FoldToken(command.TrimStart('/')));

    /// <summary>
    /// Checks a decrypted message against a query. The server's term match is
    /// only a candidate filter because truncated hashes can collide.
    /// </summary>
    public static bool Matches(string content, string query)
    {
        var queryTokens = Tokenize(query).Where(t => t.IsCjk || !StopWords.Contains(t.Text)).ToList();
        if (queryTokens.Count == 0)
            return false;

        var contentTokens = Tokenize(content).Select(t => t.Text).ToList();
        for (var i = 0; i < queryTokens.Count; i++)
        {
            var token = queryTokens[i].Text;
            var isLast = i == queryTokens.Count - 1;
            var found = isLast && !queryTokens[i].IsCjk
                ? contentTokens.Any(t => t.StartsWith(token, StringComparison.Ordinal))
                : contentTokens.Contains(token);
            if (!found)
                return false;
        }
        return true;
    }

    public static byte[] Hash(int[] terms)
    {
        var normalized = Normalize(terms ?? []);
        var buffer = new byte[normalized.Length * 4];
        for (var i = 0; i < normalized.Length; i++)
            BinaryPrimitives.WriteInt32BigEndian(buffer.AsSpan(i * 4), normalized[i]);
        return E2eeCrypto.Sha256(E2eeCrypto.Utf8("valour-e2ee/terms/v1"), buffer);
    }

    public static int[] Normalize(IEnumerable<int> terms) => terms.Distinct().Order().ToArray();

    public static int Term(byte[] indexKey, long channelId, char kind, string token)
    {
        var mac = E2eeCrypto.HmacSha256(indexKey, new E2eeWriter()
            .WriteMagic("VST1")
            .WriteInt64(channelId)
            .WriteByte((byte)kind)
            .WriteString(token)
            .ToArray());
        return BinaryPrimitives.ReadInt32BigEndian(mac);
    }

    // Tokenizing

    public readonly record struct Token(string Text, bool IsCjk);

    public static IEnumerable<Token> Tokenize(string text)
    {
        if (string.IsNullOrEmpty(text))
            yield break;

        text = StripMentions(text);
        var current = new StringBuilder();
        var cjkRun = new StringBuilder();

        foreach (var raw in text)
        {
            var c = FoldChar(raw);
            if (c == '\0')
                continue;

            if (IsCjk(c))
            {
                foreach (var token in Flush(current))
                    yield return token;
                cjkRun.Append(c);
                continue;
            }

            foreach (var token in FlushCjk(cjkRun))
                yield return token;

            if (char.IsLetterOrDigit(c))
                current.Append(c);
            else
                foreach (var token in Flush(current))
                    yield return token;
        }

        foreach (var token in Flush(current))
            yield return token;
        foreach (var token in FlushCjk(cjkRun))
            yield return token;
    }

    private static IEnumerable<Token> Flush(StringBuilder current)
    {
        if (current.Length == 0)
            yield break;
        var token = FoldToken(current.ToString());
        current.Clear();
        if (token.Length > 0 && token.Length <= 64)
            yield return new Token(token, false);
    }

    private static IEnumerable<Token> FlushCjk(StringBuilder run)
    {
        if (run.Length == 0)
            yield break;
        var text = run.ToString();
        run.Clear();
        for (var i = 0; i < text.Length; i++)
        {
            yield return new Token(text[i].ToString(), true);
            if (i + 1 < text.Length)
                yield return new Token(text.Substring(i, 2), true);
        }
    }

    /// <summary>
    /// Maps digits used as letters when a token also contains letters, and
    /// collapses long repeated runs, so "b4aaad" and "bad" fold the same way.
    /// Purely numeric tokens are left alone so numbers stay searchable.
    /// </summary>
    private static string FoldToken(string token)
    {
        var hasLetter = token.Any(char.IsLetter);
        var builder = new StringBuilder(token.Length);
        foreach (var raw in token)
        {
            var c = hasLetter ? raw switch
            {
                '0' => 'o',
                '1' => 'i',
                '3' => 'e',
                '4' => 'a',
                '5' => 's',
                '7' => 't',
                _ => raw
            } : raw;

            var length = builder.Length;
            if (length >= 2 && builder[length - 1] == c && builder[length - 2] == c)
                continue;
            builder.Append(c);
        }
        return builder.ToString();
    }

    private static string StripMentions(string text)
    {
        if (text.IndexOf('«') < 0)
            return text;

        var builder = new StringBuilder(text.Length);
        var depth = 0;
        foreach (var c in text)
        {
            if (c == '«') { depth++; builder.Append(' '); continue; }
            if (c == '»' && depth > 0) { depth--; continue; }
            if (depth == 0)
                builder.Append(c);
        }
        return builder.ToString();
    }

    internal static bool IsStopWord(string token) => StopWords.Contains(token);

    internal static string LeadingCommand(string content)
    {
        if (string.IsNullOrEmpty(content))
            return null;
        var trimmed = content.TrimStart();
        if (!trimmed.StartsWith('/'))
            return null;
        var first = Tokenize(trimmed[1..]).FirstOrDefault();
        return first.Text is null || first.IsCjk ? null : "/" + first.Text;
    }

    private static bool IsCjk(char c) =>
        c is >= '一' and <= '鿿' or >= '㐀' and <= '䶿' or >= '぀' and <= 'ヿ'
            or >= '가' and <= '힯';

    // Character folding

    private const string Latin1Upper = "ÀÁÂÃÄÅÆÇÈÉÊËÌÍÎÏÐÑÒÓÔÕÖØÙÚÛÜÝÞ";
    private const string Latin1Folded = "aaaaaaaceeeeiiiidnoooooouuuuyt";
    private const string Latin1Lower = "àáâãäåæçèéêëìíîïðñòóôõöøùúûüýþÿß";
    private const string Latin1LowerFolded = "aaaaaaaceeeeiiiidnoooooouuuuytys";

    // Latin Extended-A (U+0100 to U+017F), one folded letter per code point.
    private const string LatinExtendedAFolded =
        "aaaaaaccccccccddddeeeeeeeeeegggggggghhhhiiiiiiiiiiiijjkkkllllllllllnnnnnnnnnoooooooorrrrrrssssssssttttttuuuuuuuuuuuuwwyyyzzzzzzs";

    /// <summary>
    /// Characters that look like Latin letters, mapped so automod cannot be
    /// evaded by swapping in a lookalike from another alphabet.
    /// </summary>
    private static readonly Dictionary<char, char> Lookalikes = new()
    {
        ['а'] = 'a', ['в'] = 'b', ['е'] = 'e', ['ё'] = 'e', ['к'] = 'k', ['м'] = 'm', ['н'] = 'h', ['о'] = 'o',
        ['р'] = 'p', ['с'] = 'c', ['т'] = 't', ['у'] = 'y', ['х'] = 'x', ['і'] = 'i', ['ї'] = 'i', ['ј'] = 'j',
        ['ѕ'] = 's', ['ԁ'] = 'd', ['ԛ'] = 'q', ['ԝ'] = 'w',
        ['α'] = 'a', ['β'] = 'b', ['ε'] = 'e', ['η'] = 'n', ['ι'] = 'i', ['κ'] = 'k', ['ν'] = 'v', ['ο'] = 'o',
        ['ρ'] = 'p', ['τ'] = 't', ['υ'] = 'u', ['χ'] = 'x', ['ω'] = 'w'
    };

    /// <summary>
    /// Folds one character, or returns '\0' for characters that should be
    /// removed entirely, such as zero-width spaces used to split words.
    /// </summary>
    public static char FoldChar(char c)
    {
        if (c is '​' or '‌' or '‍' or '⁠' or '﻿' or '­' or >= '̀' and <= 'ͯ')
            return '\0';

        if (c is >= 'A' and <= 'Z')
            return (char)(c + 32);
        if (c < 0x80)
            return c;

        if (c is >= '！' and <= '～')
            return FoldChar((char)(c - 0xFEE0));

        var index = Latin1Upper.IndexOf(c);
        if (index >= 0)
            return Latin1Folded[index];
        index = Latin1Lower.IndexOf(c);
        if (index >= 0)
            return Latin1LowerFolded[index];

        if (c is >= 'Ā' and <= 'ſ')
            return LatinExtendedAFolded[c - 0x100];

        if (c is >= 'Α' and <= 'Ω')
            c = (char)(c + 32);
        else if (c is >= 'А' and <= 'Я')
            c = (char)(c + 32);
        else if (c is >= 'Ѐ' and <= 'Џ')
            c = (char)(c + 80);

        return Lookalikes.TryGetValue(c, out var mapped) ? mapped : c;
    }
}
