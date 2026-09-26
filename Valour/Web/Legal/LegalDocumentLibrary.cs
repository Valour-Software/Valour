using System.Collections.Concurrent;
using Markdig;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;

namespace Valour.Web.Legal;

public sealed record LegalDocument(
    string Key,
    string FileName,
    string Path,
    string Title,
    string Description);

public sealed record RenderedLegalDocument(LegalDocument Document, string Html);

/// <summary>
/// Renders the policy documents kept at the repository root as site pages.
/// The files are copied into the build output by Valour.Web.csproj.
/// </summary>
public sealed class LegalDocumentLibrary
{
    public static readonly LegalDocument Privacy = new(
        "privacy", "PRIVACY", "/privacy/", "Privacy Policy",
        "What data Valour collects, why it is collected, and how you can control or delete it.");

    public static readonly LegalDocument Terms = new(
        "terms", "TERMS_OF_SERVICE.md", "/terms/", "Terms of Service",
        "The terms that govern your use of Valour.");

    public static readonly LegalDocument Rules = new(
        "rules", "PLATFORM_RULES.md", "/rules/", "Platform Rules",
        "The rules that apply to everyone on official Valour systems.");

    public static readonly LegalDocument EconomyRules = new(
        "economy-rules", "PLATFORM_ECO_RULES.md", "/rules/economy/", "Economy Rules",
        "The rules for Valour's community economy and trading systems.");

    public static readonly IReadOnlyList<LegalDocument> All = [Privacy, Terms, Rules, EconomyRules];

    // Links between the documents are written as repository file names (or GitHub
    // URLs to them) so they work on GitHub; on the site they point at the pages.
    private static readonly Dictionary<string, string> PathsByFileName =
        All.ToDictionary(document => document.FileName, document => document.Path, StringComparer.OrdinalIgnoreCase);

    private static readonly MarkdownPipeline Pipeline = new MarkdownPipelineBuilder()
        .UsePipeTables()
        .UseAutoLinks()
        .UseAutoIdentifiers()
        .DisableHtml()
        .Build();

    private readonly ConcurrentDictionary<string, RenderedLegalDocument> _cache = new();

    public RenderedLegalDocument Render(LegalDocument document) =>
        _cache.GetOrAdd(document.Key, _ => RenderUncached(document));

    private static RenderedLegalDocument RenderUncached(LegalDocument document)
    {
        var filePath = System.IO.Path.Combine(AppContext.BaseDirectory, "Legal", document.FileName);
        var markdown = File.ReadAllText(filePath);
        var parsed = Markdown.Parse(markdown, Pipeline);

        foreach (var link in parsed.Descendants<LinkInline>())
        {
            if (link.Url is { } url && TryMapDocumentLink(url, out var mapped))
                link.Url = mapped;
        }

        return new RenderedLegalDocument(document, parsed.ToHtml(Pipeline));
    }

    private static bool TryMapDocumentLink(string url, out string mapped)
    {
        mapped = url;

        var fragmentIndex = url.IndexOf('#');
        var fragment = fragmentIndex >= 0 ? url[fragmentIndex..] : string.Empty;
        var target = fragmentIndex >= 0 ? url[..fragmentIndex] : url;

        if (Uri.TryCreate(target, UriKind.Absolute, out var absolute))
        {
            if (absolute.Host is not ("github.com" or "raw.githubusercontent.com"))
                return false;

            target = absolute.AbsolutePath;
        }

        var fileName = target[(target.LastIndexOf('/') + 1)..];
        if (!PathsByFileName.TryGetValue(fileName, out var path))
            return false;

        mapped = path + fragment;
        return true;
    }
}
