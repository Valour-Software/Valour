namespace Valour.Client.Emojis;

public class EmojiTrieNode
{
    public bool IsEmoji { get; set; }
    public Dictionary<int, EmojiTrieNode> Children { get; } = new();
}