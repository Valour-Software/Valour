using Valour.Server.Pages;

namespace Valour.Tests.Server;

public class PublicMarkdownRenderingTests
{
    // An event handler attribute inside a tag; the same text outside a tag is harmless
    private const string HandlerAttribute = @"<[^>]*\son[a-z]+\s*=";

    [Theory]
    [InlineData("[link](https://valour.gg){onclick=alert(1)}")]
    [InlineData("# Heading {onmouseover=alert(1)}")]
    [InlineData("![img](https://valour.gg/a.png){onerror=alert(1)}")]
    public void ThreadMarkdown_IgnoresGenericAttributes(string markdown)
    {
        var html = PublicThreadPageHelpers.RenderMarkdown(markdown);

        Assert.DoesNotMatch(HandlerAttribute, html);
    }

    [Theory]
    [InlineData("[link](https://valour.gg){onclick=alert(1)}")]
    [InlineData("## Heading {onmouseover=alert(1)}")]
    public void WikiMarkdown_IgnoresGenericAttributes(string markdown)
    {
        var html = PublicWikiPageHelpers.RenderDoc(markdown).Html;

        Assert.DoesNotMatch(HandlerAttribute, html);
    }

    [Fact]
    public void WikiMarkdown_KeepsHeadingAnchors()
    {
        var doc = PublicWikiPageHelpers.RenderDoc("## Getting Started");

        Assert.Contains("id=\"getting-started\"", doc.Html);
        Assert.Single(doc.Toc);
    }
}
