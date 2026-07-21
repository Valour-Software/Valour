namespace Valour.Client.Emojis;

/// <summary>
/// Compact, immutable emoji trie loaded from the build-generated embedded resource.
/// </summary>
public sealed class EmojiTrieNode
{
    private const uint Magic = 0x31544556; // "VET1"
    private const string ResourceName = "Valour.Client.Emojis.emoji-trie.bin";

    private readonly int[] _firstEdges;
    private readonly ushort[] _edgeCounts;
    private readonly bool[] _emojiNodes;
    private readonly int[] _edgeCodePoints;
    private readonly int[] _edgeTargets;

    private EmojiTrieNode(
        int[] firstEdges,
        ushort[] edgeCounts,
        bool[] emojiNodes,
        int[] edgeCodePoints,
        int[] edgeTargets)
    {
        _firstEdges = firstEdges;
        _edgeCounts = edgeCounts;
        _emojiNodes = emojiNodes;
        _edgeCodePoints = edgeCodePoints;
        _edgeTargets = edgeTargets;
    }

    public static EmojiTrieNode LoadEmbedded()
    {
        var assembly = typeof(EmojiTrieNode).Assembly;
        using var stream = assembly.GetManifestResourceStream(ResourceName)
            ?? throw new InvalidOperationException($"Embedded emoji trie '{ResourceName}' was not found.");
        using var reader = new BinaryReader(stream);

        if (reader.ReadUInt32() != Magic)
            throw new InvalidDataException("The embedded emoji trie has an invalid header.");

        var nodeCount = reader.ReadInt32();
        var edgeCount = reader.ReadInt32();
        if (nodeCount < 1 || nodeCount > 1_000_000 || edgeCount < 0 || edgeCount > 1_000_000)
            throw new InvalidDataException("The embedded emoji trie has invalid dimensions.");

        var firstEdges = new int[nodeCount];
        var edgeCounts = new ushort[nodeCount];
        var emojiNodes = new bool[nodeCount];
        for (var i = 0; i < nodeCount; i++)
        {
            firstEdges[i] = reader.ReadInt32();
            edgeCounts[i] = reader.ReadUInt16();
            emojiNodes[i] = reader.ReadBoolean();
        }

        var edgeCodePoints = new int[edgeCount];
        var edgeTargets = new int[edgeCount];
        for (var i = 0; i < edgeCount; i++)
        {
            edgeCodePoints[i] = reader.ReadInt32();
            edgeTargets[i] = reader.ReadInt32();
        }

        if (stream.Position != stream.Length)
            throw new InvalidDataException("The embedded emoji trie contains trailing data.");

        return new EmojiTrieNode(firstEdges, edgeCounts, emojiNodes, edgeCodePoints, edgeTargets);
    }

    public int Match(ReadOnlySpan<int> codePoints)
    {
        var nodeIndex = 0;
        var lastMatch = -1;

        for (var i = 0; i < codePoints.Length; i++)
        {
            var edgeIndex = FindEdge(nodeIndex, codePoints[i]);
            if (edgeIndex < 0)
                break;

            nodeIndex = _edgeTargets[edgeIndex];
            if (_emojiNodes[nodeIndex])
                lastMatch = i;
        }

        return lastMatch + 1;
    }

    private int FindEdge(int nodeIndex, int codePoint)
    {
        var low = _firstEdges[nodeIndex];
        var high = low + _edgeCounts[nodeIndex] - 1;

        while (low <= high)
        {
            var middle = low + ((high - low) / 2);
            var candidate = _edgeCodePoints[middle];
            if (candidate == codePoint)
                return middle;

            if (candidate < codePoint)
                low = middle + 1;
            else
                high = middle - 1;
        }

        return -1;
    }
}
