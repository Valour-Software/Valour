using Valour.Client.Emojis;

namespace Valour.Tests.Client;

public class EmojiTrieTests
{
    private readonly EmojiTrieNode _trie = EmojiTrieNode.LoadEmbedded();

    [Fact]
    public void MatchesSingleCodePointEmoji()
    {
        Assert.Equal(1, _trie.Match([0x1F600]));
    }

    [Fact]
    public void MatchesFullyQualifiedEmojiSequence()
    {
        Assert.Equal(7, _trie.Match([0x1F468, 0x200D, 0x1F469, 0x200D, 0x1F467, 0x200D, 0x1F466]));
    }

    [Fact]
    public void DoesNotMatchNonEmojiText()
    {
        Assert.Equal(0, _trie.Match(['A']));
    }
}
