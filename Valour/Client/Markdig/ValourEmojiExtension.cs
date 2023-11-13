using Markdig;
using Markdig.Extensions.Emoji;
using Markdig.Renderers;

namespace Valour.Client.Markdig;

/// <summary>
/// Extension to allow emoji shortcodes and smileys replacement.
/// </summary>
/// <seealso cref="IMarkdownExtension" />
public class ValourEmojiExtension : IMarkdownExtension
{
    public ValourEmojiExtension(ValourEmojiMapping emojiMapping)
    {
        EmojiMapping = emojiMapping;
    }

    public ValourEmojiMapping EmojiMapping { get; }

    public void Setup(MarkdownPipelineBuilder pipeline)
    {
        if (!pipeline.InlineParsers.Contains<ValourEmojiParser>())
        {
            // Insert the parser before any other parsers
            pipeline.InlineParsers.Insert(0, new ValourEmojiParser(EmojiMapping));
        }
    }

    public void Setup(MarkdownPipeline pipeline, IMarkdownRenderer renderer)
    {
    }
}

public static class ValourEmojiMarkdownExtension
{
    public static MarkdownPipelineBuilder UseValourEmojiExtension(this MarkdownPipelineBuilder pipeline, bool enableAutoSmileys)
    {
        pipeline.Extensions.AddIfNotAlready(new ValourEmojiExtension(new ValourEmojiMapping(enableAutoSmileys)));
        return pipeline;
    }
}