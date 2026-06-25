using Markdig.Blazor;
using Markdig.Syntax.Inlines;
using Valour.Client.Components.Messages;
using Valour.Shared.Utilities;

namespace Valour.Client.Markdig;

/// <summary>
/// Renders markdown links. Valour app links (channels, threads, DMs, etc.) become
/// in-app navigation so they open inside the app instead of a new browser tab;
/// everything else falls back to the default external-link behavior.
/// </summary>
public class ValourLinkRenderer : BlazorObjectRenderer<LinkInline>
{
    protected override void Write(BlazorRenderer renderer, LinkInline link)
    {
        if (renderer == null) throw new ArgumentNullException(nameof(renderer));
        if (link == null) throw new ArgumentNullException(nameof(link));

        var url = (link.GetDynamicUrl != null ? link.GetDynamicUrl() ?? link.Url : link.Url) ?? string.Empty;

        if (!Uri.IsWellFormedUriString(url, UriKind.RelativeOrAbsolute))
            url = "#";

        // Images and non-Valour links keep the default external-link rendering.
        if (link.IsImage || !ValourRouteParser.IsValourAppLink(url))
        {
            renderer.OpenElement("a")
                .AddAttribute("href", url, 1)
                .AddAttribute("rel", "noopener noreferrer nofollow", 2)
                .AddAttribute("target", "_blank", 3)
                .WriteText(url, 4)
                .CloseElement();
            return;
        }

        renderer.OpenComponent<ValourLink>()
            .AddComponentParam("Url", url)
            .CloseComponent();
    }
}
