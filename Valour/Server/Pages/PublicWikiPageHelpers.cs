#nullable enable annotations

using System.Text;
using Markdig;
using Markdig.Extensions.AutoIdentifiers;
using Markdig.Renderers;
using Markdig.Renderers.Html;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;
using Microsoft.EntityFrameworkCore;
using Valour.Client.Components.Wiki.Display;
using Valour.Config.Configs;
using Valour.Server.Database;

namespace Valour.Server.Pages;

public class DocTocEntry
{
    public int Level { get; set; }
    public string Text { get; set; } = string.Empty;
    public string? Id { get; set; }
}

public class RenderedDoc
{
    public string Html { get; set; } = string.Empty;
    public List<DocTocEntry> Toc { get; set; } = new();
}

/// <summary>
/// Shared rendering helpers for the public (non-Blazor) docs pages
/// </summary>
public static class PublicWikiPageHelpers
{
    // GitHub-style heading ids must be registered before UseAdvancedExtensions
    // adds the default variant; DisableHtml keeps user markdown safe to render
    // server-side (same approach as the public thread pages).
    private static readonly MarkdownPipeline Pipeline = new MarkdownPipelineBuilder()
        .UseAutoIdentifiers(AutoIdentifierOptions.GitHub)
        .UseAdvancedExtensions()
        .DisableHtml()
        .Build();

    /// <summary>
    /// True when the docs host is its own subdomain. In single-domain
    /// self-host mode the docs pages are served under the /docs path instead.
    /// </summary>
    public static bool WikiHostIsDistinct =>
        !string.Equals(HostingConfig.Current.WikiHost, HostingConfig.Current.AppHost, StringComparison.OrdinalIgnoreCase) &&
        !string.Equals(HostingConfig.Current.WikiHost, HostingConfig.Current.RootDomain, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Base URL public docs links are built against, e.g.
    /// "https://wiki.valour.gg" or "https://my-host/wiki".
    /// </summary>
    public static string PublicWikiBase => WikiHostIsDistinct
        ? HostingConfig.Current.WikiBaseUrl
        : $"{HostingConfig.Current.WikiBaseUrl}/wiki";

    /// <summary>
    /// The URL path segment identifying a planet in public docs URLs — the
    /// claimed vanity when present, otherwise the planet id.
    /// </summary>
    public static string GetPlanetSegment(Valour.Database.Planet planet) =>
        string.IsNullOrWhiteSpace(planet.Vanity) ? planet.Id.ToString() : planet.Vanity;

    public static string GetPageUrl(Valour.Database.Planet planet, string slug) =>
        $"{PublicWikiBase}/{GetPlanetSegment(planet)}/{slug}";

    public static string GetHomeUrl(Valour.Database.Planet planet) =>
        $"{PublicWikiBase}/{GetPlanetSegment(planet)}";

    /// <summary>
    /// Resolves the {planetIdOrVanity} route segment. Vanity names can never
    /// be all digits, so a numeric segment is always a planet id. The global
    /// soft-delete query filter excludes deleted planets.
    /// </summary>
    public static async Task<Valour.Database.Planet?> ResolvePlanetAsync(ValourDb db, string? planetIdOrVanity)
    {
        if (string.IsNullOrWhiteSpace(planetIdOrVanity))
            return null;

        if (long.TryParse(planetIdOrVanity, out var planetId))
            return await db.Planets.AsNoTracking().FirstOrDefaultAsync(x => x.Id == planetId);

        var vanity = planetIdOrVanity.Trim().ToLowerInvariant();
        return await db.Planets.AsNoTracking().FirstOrDefaultAsync(x => x.Vanity == vanity);
    }

    /// <summary>
    /// Renders doc markdown to sanitized HTML and extracts the table of
    /// contents (heading levels 2-3) in one parse.
    /// </summary>
    public static RenderedDoc RenderDoc(string? content)
    {
        var result = new RenderedDoc();

        var cleaned = PublicThreadPageHelpers.CleanTokens(content);
        if (string.IsNullOrWhiteSpace(cleaned))
            return result;

        try
        {
            var document = Markdown.Parse(cleaned, Pipeline);

            foreach (var heading in document.Descendants<HeadingBlock>())
            {
                if (heading.Level is < 2 or > 3)
                    continue;

                var text = ExtractText(heading.Inline);
                if (string.IsNullOrWhiteSpace(text))
                    continue;

                result.Toc.Add(new DocTocEntry
                {
                    Level = heading.Level,
                    Text = text,
                    Id = heading.TryGetAttributes()?.Id,
                });
            }

            var writer = new StringWriter();
            var renderer = new HtmlRenderer(writer);
            Pipeline.Setup(renderer);
            renderer.Render(document);
            writer.Flush();

            result.Html = writer.ToString();
        }
        catch
        {
            result.Html = System.Net.WebUtility.HtmlEncode(cleaned);
            result.Toc.Clear();
        }

        return result;
    }

    public static string ToPlainSnippet(string? content, int maxLength = 200) =>
        PublicThreadPageHelpers.ToPlainSnippet(content, maxLength);

    /// <summary>
    /// Builds the display tree consumed by WikiTreeDisplay from the flat,
    /// position-ordered metadata list.
    /// </summary>
    public static List<WikiTreeNodeData> BuildTree(
        IReadOnlyList<Models.PlanetWikiPage> docs,
        Valour.Database.Planet planet,
        string? activeSlug)
    {
        var byParent = docs.ToLookup(x => x.ParentId);

        List<WikiTreeNodeData> BuildLevel(long? parentId) =>
            byParent[parentId]
                .OrderBy(x => x.Position)
                .Select(x => new WikiTreeNodeData
                {
                    Id = x.Id,
                    Title = x.Title,
                    IsFolder = x.IsFolder,
                    Href = x.IsFolder || x.Slug is null ? null : GetPageUrl(planet, x.Slug),
                    IsActive = !x.IsFolder && x.Slug is not null && x.Slug == activeSlug,
                    IsExpanded = true,
                    Children = BuildLevel(x.Id),
                })
                .ToList();

        return BuildLevel(null);
    }

    /// <summary>
    /// Pages in reading order (pre-order traversal), used for prev/next links
    /// </summary>
    public static List<Models.PlanetWikiPage> FlattenPages(IReadOnlyList<Models.PlanetWikiPage> docs)
    {
        var byParent = docs.ToLookup(x => x.ParentId);
        var result = new List<Models.PlanetWikiPage>();

        void Walk(long? parentId)
        {
            foreach (var node in byParent[parentId].OrderBy(x => x.Position))
            {
                if (!node.IsFolder)
                    result.Add(node);
                Walk(node.Id);
            }
        }

        Walk(null);
        return result;
    }

    private static string ExtractText(ContainerInline? inline)
    {
        if (inline is null)
            return string.Empty;

        var sb = new StringBuilder();
        foreach (var child in inline)
        {
            switch (child)
            {
                case LiteralInline literal:
                    sb.Append(literal.Content.ToString());
                    break;
                case CodeInline code:
                    sb.Append(code.Content);
                    break;
                case ContainerInline container:
                    sb.Append(ExtractText(container));
                    break;
            }
        }

        return sb.ToString();
    }
}
