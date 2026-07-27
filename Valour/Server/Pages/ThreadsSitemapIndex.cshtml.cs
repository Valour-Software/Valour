#nullable enable annotations

using System.Text;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using Valour.Config.Configs;
using Valour.Server.Database;

namespace Valour.Server.Pages;

/// <summary>
/// Sitemap index for the public threads site — one per-planet sitemap entry
/// for every planet with public threads, so crawlers can discover all of them
/// from the robots.txt Sitemap line
/// </summary>
public class ThreadsSitemapIndexModel : PageModel
{
    private readonly ValourDb _db;

    public ThreadsSitemapIndexModel(ValourDb db)
    {
        _db = db;
    }

    public async Task<IActionResult> OnGetAsync()
    {
        var planetIds = await _db.Planets.AsNoTracking()
            .Where(x => x.EnableThreads && x.PublicThreads && !x.IsDeleted && !x.Nsfw)
            .Select(x => x.Id)
            .ToListAsync();

        var sb = new StringBuilder();
        sb.AppendLine("<?xml version=\"1.0\" encoding=\"UTF-8\"?>");
        sb.AppendLine("<sitemapindex xmlns=\"http://www.sitemaps.org/schemas/sitemap/0.9\">");

        foreach (var planetId in planetIds)
        {
            sb.AppendLine("  <sitemap>");
            sb.AppendLine($"    <loc>{HostingConfig.Current.ThreadsBaseUrl}/{planetId}/sitemap.xml</loc>");
            sb.AppendLine("  </sitemap>");
        }

        sb.AppendLine("</sitemapindex>");

        Response.Headers.CacheControl = "public, max-age=3600";
        return Content(sb.ToString(), "application/xml", Encoding.UTF8);
    }
}
