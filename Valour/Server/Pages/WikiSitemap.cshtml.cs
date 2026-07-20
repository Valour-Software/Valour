#nullable enable annotations

using System.Text;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Valour.Server.Database;
using Valour.Server.Services;

namespace Valour.Server.Pages;

/// <summary>
/// Per-planet sitemap for the public docs site
/// </summary>
public class WikiSitemapModel : PageModel
{
    private readonly ValourDb _db;
    private readonly PlanetWikiService _wikiService;

    public WikiSitemapModel(ValourDb db, PlanetWikiService wikiService)
    {
        _db = db;
        _wikiService = wikiService;
    }

    [BindProperty(SupportsGet = true)]
    public string? PlanetIdOrVanity { get; set; }

    public async Task<IActionResult> OnGetAsync()
    {
        var planet = await PublicWikiPageHelpers.ResolvePlanetAsync(_db, PlanetIdOrVanity);
        if (planet is null || !planet.EnableWiki || !planet.PublicWiki)
            return NotFound();

        var docs = await _wikiService.GetTreeAsync(planet.Id, publishedOnly: true);

        var sb = new StringBuilder();
        sb.AppendLine("<?xml version=\"1.0\" encoding=\"UTF-8\"?>");
        sb.AppendLine("<urlset xmlns=\"http://www.sitemaps.org/schemas/sitemap/0.9\">");

        AppendUrl(sb, PublicWikiPageHelpers.GetHomeUrl(planet), null);

        foreach (var doc in docs.Where(x => !x.IsFolder && x.Slug is not null))
            AppendUrl(sb, PublicWikiPageHelpers.GetPageUrl(planet, doc.Slug), doc.LastEdited ?? doc.TimeCreated);

        sb.AppendLine("</urlset>");

        Response.Headers.CacheControl = "public, max-age=300";
        return Content(sb.ToString(), "application/xml", Encoding.UTF8);
    }

    private static void AppendUrl(StringBuilder sb, string url, DateTime? lastMod)
    {
        sb.AppendLine("  <url>");
        sb.AppendLine($"    <loc>{System.Security.SecurityElement.Escape(url)}</loc>");
        if (lastMod is not null)
            sb.AppendLine($"    <lastmod>{lastMod.Value:yyyy-MM-dd}</lastmod>");
        sb.AppendLine("  </url>");
    }
}
