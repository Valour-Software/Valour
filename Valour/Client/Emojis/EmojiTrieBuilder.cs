using System.Text.RegularExpressions;
using Valour.Sdk.Client;

namespace Valour.Client.Emojis;

public static class EmojiTrieBuilder
{
    private const string EmojiUrl = "_content/Valour.Client/emoji-test.txt"; // If the URL fails, use this backup

    public static EmojiTrieNode RootNode;
    
    public static async Task SetupTrieAsync(ValourClient client)
    {
        Console.WriteLine("Setting up emoji trie...");
        RootNode = await BuildTrieFromUnicodeAsync(client);
        Console.WriteLine("Emoji trie setup complete.");
    }

    public static async Task<EmojiTrieNode> BuildTrieFromUnicodeAsync(ValourClient client)
    {
        var emojiSequences = new List<int[]>();

        using (var http = new HttpClient())
        {
            string? emojiTest = null;

            try
            {
                // If the request fails, fall back to the local backup
                emojiTest = await client.Http.GetStringAsync(EmojiUrl);
            }
            catch (HttpRequestException)
            {
                Console.WriteLine("Failed to fetch emoji test file. Emojis will not work properly!");
            }

            if (emojiTest == null)
            {
                return new EmojiTrieNode();
            }

            // Regex to match lines like: "1F468 200D 1F469 200D 1F467 200D 1F466 ; fully-qualified"
            var regex = new Regex(@"^([0-9A-F ]+)\s*;\s*fully-qualified", RegexOptions.Compiled);

            foreach (var line in emojiTest.Split('\n'))
            {
                var match = regex.Match(line);
                if (match.Success)
                {
                    var codePoints = match.Groups[1].Value
                        .Split(' ', StringSplitOptions.RemoveEmptyEntries);
                    var intCodePoints = Array.ConvertAll(codePoints, cp => Convert.ToInt32(cp, 16));
                    emojiSequences.Add(intCodePoints);
                }
            }
        }

        // Build the Trie
        var root = new EmojiTrieNode();
        foreach (var seq in emojiSequences)
        {
            AddEmojiSequence(root, seq);
        }
        return root;
    }

    private static void AddEmojiSequence(EmojiTrieNode root, int[] codePoints)
    {
        var node = root;
        foreach (var cp in codePoints)
        {
            if (!node.Children.TryGetValue(cp, out var child))
            {
                child = new EmojiTrieNode();
                node.Children[cp] = child;
            }
            node = child;
        }
        node.IsEmoji = true;
    }
}