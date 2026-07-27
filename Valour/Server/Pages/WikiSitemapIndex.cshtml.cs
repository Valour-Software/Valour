#nullable enable annotations

using System.Text;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using Valour.Server.Database;

namespace Valour.Server.Pages;

/// <summary>
/// Sitemap index for the public wiki site — points crawlers at every
/// public planet wiki's own sitemap
/// </summary>
public class WikiSitemapIndexModel : PageModel
{
    private readonly ValourDb _db;

    public WikiSitemapIndexModel(ValourDb db)
    {
        _db = db;
    }

    public async Task<IActionResult> OnGetAsync()
    {
        var planets = await _db.Planets.AsNoTracking()
            .Where(x => x.EnableWiki && x.PublicWiki && !x.IsDeleted && !x.Nsfw)
            .Select(x => new { x.Id, x.Vanity })
            .ToListAsync();

        var sb = new StringBuilder();
        sb.AppendLine("<?xml version=\"1.0\" encoding=\"UTF-8\"?>");
        sb.AppendLine("<sitemapindex xmlns=\"http://www.sitemaps.org/schemas/sitemap/0.9\">");

        foreach (var planet in planets)
        {
            var segment = string.IsNullOrWhiteSpace(planet.Vanity)
                ? planet.Id.ToString()
                : planet.Vanity;

            sb.AppendLine("  <sitemap>");
            sb.AppendLine($"    <loc>{System.Security.SecurityElement.Escape($"{PublicWikiPageHelpers.PublicWikiBase}/{segment}/sitemap.xml")}</loc>");
            sb.AppendLine("  </sitemap>");
        }

        sb.AppendLine("</sitemapindex>");

        Response.Headers.CacheControl = "public, max-age=3600";
        return Content(sb.ToString(), "application/xml", Encoding.UTF8);
    }
}
