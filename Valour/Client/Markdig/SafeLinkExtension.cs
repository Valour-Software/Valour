using Markdig;
using Markdig.Renderers;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;
using Valour.Shared.Utilities;

namespace Valour.Client.Markdig;

/// <summary>
/// Strips dangerous URL schemes from every link and image in a parsed document.
///
/// This runs at the document level rather than in a renderer on purpose: the
/// same markdown is rendered by several different renderers (the Blazor
/// renderer for chat, the plain HTML renderer for embeds, and the server-side
/// pipelines for public wiki and thread pages). A renderer-level guard only
/// protects whichever renderer it was registered on, which is exactly how
/// javascript: links reached the non-chat surfaces. Sanitizing the AST covers
/// all of them at once.
/// </summary>
public class SafeLinkExtension : IMarkdownExtension
{
    public void Setup(MarkdownPipelineBuilder pipeline)
    {
        // Guard against double-registration re-running the pass.
        pipeline.DocumentProcessed -= SanitizeDocument;
        pipeline.DocumentProcessed += SanitizeDocument;
    }

    public void Setup(MarkdownPipeline pipeline, IMarkdownRenderer renderer)
    {
    }

    private static void SanitizeDocument(MarkdownDocument document)
    {
        foreach (var link in document.Descendants<LinkInline>())
        {
            link.Url = SafeUrl.Sanitize(link.Url);

            // Extensions can swap in a URL at render time, which would bypass
            // the check above, so wrap the resolver rather than trusting it.
            var dynamicUrl = link.GetDynamicUrl;
            if (dynamicUrl is not null)
                link.GetDynamicUrl = () => SafeUrl.Sanitize(dynamicUrl());
        }
    }
}

public static class SafeLinkExtensionHelper
{
    /// <summary>
    /// Applies link-scheme allowlisting to the pipeline. Every pipeline that
    /// renders user-authored markdown must call this.
    /// </summary>
    public static MarkdownPipelineBuilder UseSafeLinks(this MarkdownPipelineBuilder pipeline)
    {
        pipeline.Extensions.AddIfNotAlready<SafeLinkExtension>();
        return pipeline;
    }
}
