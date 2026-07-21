namespace Valour.Client.Emojis;

/// <summary>
/// Provides the build-generated emoji trie to the markdown parsers.
/// </summary>
public static class EmojiTrieBuilder
{
    public static EmojiTrieNode RootNode { get; } = EmojiTrieNode.LoadEmbedded();
}
