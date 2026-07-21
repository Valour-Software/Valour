// Usage: EmojiTrieCompiler <emoji-test.txt> <output.bin>

if (args.Length != 2)
{
    Console.Error.WriteLine("Usage: EmojiTrieCompiler <emoji-test.txt> <output.bin>");
    return 1;
}

var inputPath = args[0];
var outputPath = args[1];

if (!File.Exists(inputPath))
{
    Console.Error.WriteLine($"Emoji trie input not found: {inputPath}");
    return 1;
}

var nodes = new List<TrieNode> { new() };
var sequenceCount = 0;

foreach (var line in File.ReadLines(inputPath))
{
    var separatorIndex = line.IndexOf(';');
    if (separatorIndex < 1 ||
        !line.AsSpan(separatorIndex + 1).TrimStart()
            .StartsWith("fully-qualified", StringComparison.Ordinal))
        continue;

    var nodeIndex = 0;
    foreach (var value in line[..separatorIndex]
                 .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
    {
        var codePoint = Convert.ToInt32(value, 16);
        if (!nodes[nodeIndex].Children.TryGetValue(codePoint, out var childIndex))
        {
            childIndex = nodes.Count;
            nodes[nodeIndex].Children.Add(codePoint, childIndex);
            nodes.Add(new TrieNode());
        }

        nodeIndex = childIndex;
    }

    nodes[nodeIndex].IsEmoji = true;
    sequenceCount++;
}

var edgeCount = nodes.Sum(node => node.Children.Count);

using var buffer = new MemoryStream();
using (var writer = new BinaryWriter(buffer, System.Text.Encoding.UTF8, leaveOpen: true))
{
    writer.Write(0x31544556u); // "VET1" in little-endian form
    writer.Write(nodes.Count);
    writer.Write(edgeCount);

    var firstEdge = 0;
    foreach (var node in nodes)
    {
        if (node.Children.Count > ushort.MaxValue)
            throw new InvalidOperationException("An emoji trie node has too many children.");

        writer.Write(firstEdge);
        writer.Write((ushort)node.Children.Count);
        writer.Write(node.IsEmoji);
        firstEdge += node.Children.Count;
    }

    foreach (var node in nodes)
    {
        foreach (var (codePoint, targetNode) in node.Children)
        {
            writer.Write(codePoint);
            writer.Write(targetNode);
        }
    }
}

var compiled = buffer.ToArray();
Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
File.WriteAllBytes(outputPath, compiled);

Console.WriteLine(
    $"Emoji trie generated: {outputPath} " +
    $"({sequenceCount} sequences, {nodes.Count} nodes, {edgeCount} edges, {compiled.Length} bytes)");

return 0;

internal sealed class TrieNode
{
    public bool IsEmoji { get; set; }
    public SortedDictionary<int, int> Children { get; } = new();
}
