using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Valour.Client.Messages;

namespace Valour.Tests.Client;

public class MarkdownLinkRenderingTests
{
    [Theory]
    [InlineData("**Hello** [village](https://example.com)", "Hello village")]
    [InlineData("«@m-123» waved «e-:party_blob:~456»", "@user waved :party_blob:")]
    [InlineData("A   message\nwith spacing", "A message with spacing")]
    [InlineData("Hi 🙂", "Hi 🙂")]
    [InlineData("That is ||classified||", "That is classified")]
    [InlineData("Watching $SPY today", "Watching $SPY today")]
    public void PlainTextProjection_UsesMessagePipeline(string markdown, string expected)
    {
        Assert.Equal(expected, MarkdownManager.GetPlainText(markdown));
    }

    [Fact]
    public void PlainTextProjection_TruncatesForCompactSurfaces()
    {
        Assert.Equal("1234567\u2026", MarkdownManager.GetPlainText("1234567890", 8));
        Assert.Equal("🙂🙂🙂\u2026", MarkdownManager.GetPlainText("🙂🙂🙂🙂🙂", 4));
    }

    [Theory]
    [InlineData("https://example.com/path")]
    [InlineData("http://example.com/path")]
    public async Task ExternalLink_RendersAsPassiveHardenedAnchor(string url)
    {
        var html = await RenderAsync($"[Example]({url})");

        Assert.Contains($"href=\"{url}\"", html);
        Assert.Contains("target=\"_blank\"", html);
        Assert.Contains("rel=\"noopener noreferrer nofollow\"", html);
        Assert.DoesNotContain("onclick", html, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("javascript:alert(1)")]
    [InlineData("data:text/html,test")]
    [InlineData("file:///etc/passwd")]
    public async Task UnsafeExternalLink_DoesNotRenderNavigableTarget(string url)
    {
        var html = await RenderAsync($"[Example]({url})");

        Assert.DoesNotContain($"href=\"{url}\"", html, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("<javascript:alert(1)>")]
    [InlineData("<JaVaScRiPt:alert(1)>")]
    [InlineData("<vbscript:msgbox(1)>")]
    public async Task UnsafeAutolink_RendersAsText(string markdown)
    {
        var html = await RenderAsync(markdown);

        Assert.DoesNotContain("href", html, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task RepeatedRender_ReusesParsedDocumentWithIdenticalOutput()
    {
        const string markdown = "**bold** ||spoiler|| [link](https://example.com)";

        var first = await RenderAsync(markdown);
        var second = await RenderAsync(markdown);

        Assert.Equal(first, second);
        Assert.Contains("<strong>bold</strong>", first);
        Assert.Contains("md-spoiler", first);
    }

    [Fact]
    public void GetHtml_ReturnsStableOutputAcrossCalls()
    {
        const string markdown = "# Title\n\n*emphasis* and `code`";

        var first = MarkdownManager.GetHtml(markdown);
        var second = MarkdownManager.GetHtml(markdown);

        Assert.Equal(first, second);
        Assert.Contains("<em>emphasis</em>", first);
        Assert.Equal("", MarkdownManager.GetHtml(null!));
    }

    [Fact]
    public void GetHtml_AfterPipelineRegeneration_StillRendersContent()
    {
        const string markdown = "plain **text**";

        var before = MarkdownManager.GetHtml(markdown);
        MarkdownManager.RegenPipeline();
        var after = MarkdownManager.GetHtml(markdown);

        Assert.Equal(before, after);
    }

    [Fact]
    public void BoundedLruCache_EvictsLeastRecentlyUsedEntry()
    {
        var cache = new BoundedLruCache<string, int>(2);
        cache.Set("a", 1);
        cache.Set("b", 2);
        Assert.True(cache.TryGet("a", out _));

        cache.Set("c", 3);

        Assert.Equal(2, cache.Count);
        Assert.True(cache.TryGet("a", out var a));
        Assert.Equal(1, a);
        Assert.False(cache.TryGet("b", out _));
        Assert.True(cache.TryGet("c", out _));
    }

    [Fact]
    public void BoundedLruCache_SetOnExistingKeyReplacesValueWithoutGrowing()
    {
        var cache = new BoundedLruCache<string, int>(2);
        cache.Set("a", 1);
        cache.Set("a", 5);

        Assert.Equal(1, cache.Count);
        Assert.True(cache.TryGet("a", out var value));
        Assert.Equal(5, value);
    }

    private static async Task<string> RenderAsync(string markdown)
    {
        var services = new ServiceCollection()
            .AddLogging()
            .BuildServiceProvider();
        await using var renderer = new HtmlRenderer(
            services,
            services.GetRequiredService<ILoggerFactory>());

        return await renderer.Dispatcher.InvokeAsync(async () =>
        {
            var component = await renderer.RenderComponentAsync<MarkdownFragment>(
                ParameterView.FromDictionary(new Dictionary<string, object?>
                {
                    [nameof(MarkdownFragment.Content)] = markdown
                }));

            return component.ToHtmlString();
        });
    }

    private sealed class MarkdownFragment : ComponentBase
    {
        [Parameter]
        public string Content { get; set; } = string.Empty;

        protected override void BuildRenderTree(RenderTreeBuilder builder)
        {
            MarkdownManager.RenderToFragment(Content, builder, this);
        }
    }
}
