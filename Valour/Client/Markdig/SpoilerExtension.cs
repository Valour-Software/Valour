using Markdig;
using Markdig.Parsers.Inlines;
using Markdig.Renderers;

namespace Valour.Client.Markdig;

/// <summary>
/// A markdown extension for parsing ||spoiler|| text. Requires UseEmphasisExtras()
/// (or any other extension registering EmphasisInlineParser) to already be present
/// in the pipeline, since it hooks into that parser rather than adding a new one.
/// </summary>
public class SpoilerExtension : IMarkdownExtension
{
    public void Setup(MarkdownPipelineBuilder pipeline)
    {
        var emphasisParser = pipeline.InlineParsers.Find<EmphasisInlineParser>();
        if (emphasisParser is null)
            return;

        if (!emphasisParser.HasEmphasisChar('|'))
            emphasisParser.EmphasisDescriptors.Add(new EmphasisDescriptor('|', 2, 2, false));

        emphasisParser.TryCreateEmphasisInlineList.Add((delimiterChar, delimiterCount) =>
            delimiterChar == '|' ? new SpoilerInline() : null);
    }

    public void Setup(MarkdownPipeline pipeline, IMarkdownRenderer renderer)
    {
    }
}

public static class SpoilerMarkdownExtension
{
    public static MarkdownPipelineBuilder UseSpoilerExtension(this MarkdownPipelineBuilder pipeline)
    {
        pipeline.Extensions.AddIfNotAlready<SpoilerExtension>();
        return pipeline;
    }
}
