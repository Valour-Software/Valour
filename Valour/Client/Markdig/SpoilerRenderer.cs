using System.Text.RegularExpressions;
using Markdig.Blazor;
using Markdig.Syntax.Inlines;
using Valour.Shared.Cdn;

namespace Valour.Client.Markdig;

public class SpoilerRenderer : BlazorObjectRenderer<SpoilerInline>
{
    protected override void Write(BlazorRenderer renderer, SpoilerInline obj)
    {
        if (renderer == null) throw new ArgumentNullException(nameof(renderer));
        if (obj == null) throw new ArgumentNullException(nameof(obj));

        renderer.OpenElement("span", 0);
        renderer.AddAttribute("class", "md-spoiler", 1);
        renderer.AddAttribute("role", "button", 2);
        renderer.AddAttribute("tabindex", "0", 3);
        // Plain inline onclick instead of a Blazor event handler.
        // The reveal is purely visual, and this way it still works
        // in ghost/preview renders that never wire up Blazor's event dispatch.
        renderer.AddAttribute("onclick", "this.classList.toggle('revealed')", 4);

        foreach (var child in obj)
        {
            WriteInlineWithAutoLinks(renderer, child);
        }

        renderer.CloseElement();
    }

    /// <summary>
    /// Markdig's autolink parser won't pick up a URL sitting right against a custom emphasis delimiter,
    /// so a bare link at the start/end of a spoiler never gets auto-linked normally. 
    /// Find and link those manually here using the same URL pattern the server uses for embeds,
    /// routed through the normal renderer.Render path so it still gets in-app link handling.
    /// </summary>
    private static void WriteInlineWithAutoLinks(BlazorRenderer renderer, Inline child)
    {
        if (child is not LiteralInline literal)
        {
            renderer.Render(child);
            return;
        }

        var text = literal.Content.ToString();
        var matches = CdnUtils.UrlRegex.Matches(text);
        if (matches.Count == 0)
        {
            renderer.Render(child);
            return;
        }

        var cursor = 0;
        foreach (Match match in matches)
        {
            if (match.Index > cursor)
                renderer.WriteText(text.Substring(cursor, match.Index - cursor), 0);

            var link = new LinkInline
            {
                Url = match.Value,
                IsAutoLink = true,
            };
            link.AppendChild(new LiteralInline(match.Value));
            renderer.Render(link);

            cursor = match.Index + match.Length;
        }

        if (cursor < text.Length)
            renderer.WriteText(text.Substring(cursor), 0);
    }
}
