using Markdig;
using Markdig.Renderers;

namespace Valour.Client.Markdig;

public class StockExtension : IMarkdownExtension
{
    public StockExtension()
    {
    }
    
    public void Setup(MarkdownPipelineBuilder pipeline)
    {
        pipeline.InlineParsers.AddIfNotAlready<StockParser>();
    }
    
    public void Setup(MarkdownPipeline pipeline, IMarkdownRenderer renderer)
    {
    }
}

public static class StockMarkdownExtension
{
    public static MarkdownPipelineBuilder UseStockExtension(this MarkdownPipelineBuilder pipeline)
    {
        pipeline.Extensions.AddIfNotAlready<StockExtension>();
        return pipeline;
    }
}