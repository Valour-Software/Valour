using Markdig;
using Markdig.Renderers;

namespace Valour.Client.Markdig;

/// <summary>
/// A markdown extension for parsing Valour mentions
/// </summary>
public class MentionExtension : IMarkdownExtension
{
    public MentionExtension()
    {
    }
    
    public void Setup(MarkdownPipelineBuilder pipeline)
    {
        pipeline.InlineParsers.AddIfNotAlready<MentionParser>();
    }
    
    public void Setup(MarkdownPipeline pipeline, IMarkdownRenderer renderer)
    {
    }
}

public static class MentionMarkdownExtension
{
    public static MarkdownPipelineBuilder UseMentionExtension(this MarkdownPipelineBuilder pipeline)
    {
        pipeline.Extensions.AddIfNotAlready<MentionExtension>();
        return pipeline;
    }
}