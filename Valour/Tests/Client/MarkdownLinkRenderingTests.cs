using Markdig.Blazor;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Valour.Client.Messages;
using Markdown = Markdig.Blazor.Markdown;

namespace Valour.Tests.Client;

public class MarkdownLinkRenderingTests
{
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
            Markdown.RenderToFragment(
                Content,
                builder,
                MarkdownManager.Pipeline,
                MarkdownManager.Renderer,
                this);
        }
    }
}
