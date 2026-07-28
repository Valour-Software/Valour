#nullable enable annotations

using System.Text;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using Valour.Config.Configs;
using Valour.Server.Database;

namespace Valour.Server.Pages;

/// <summary>
/// Per-planet sitemap for the public threads site
/// </summary>
public class PlanetThreadsSitemapModel : PageModel
{
    // Newest N threads per planet; older content stays reachable through
    // the paginated index pages
    private const int MaxThreads = 5000;

    private readonly ValourDb _db;

    public PlanetThreadsSitemapModel(ValourDb db)
    {
        _db = db;
    }

    [BindProperty(SupportsGet = true)]
    public long PlanetId { get; set; }

    public async Task<IActionResult> OnGetAsync()
    {
        var planet = await _db.Planets.AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == PlanetId && !x.IsDeleted);

        if (planet is null || !planet.EnableThreads || !planet.PublicThreads)
            return NotFound();

        var threads = await _db.PlanetThreads.AsNoTracking()
            .Where(x => x.PlanetId == PlanetId && !x.IsDeleted && !x.Nsfw)
            .OrderByDescending(x => x.TimeCreated)
            .Take(MaxThreads)
            .Select(x => new { x.Id, x.TimeCreated, x.EditedTime })
            .ToListAsync();

        var baseUrl = HostingConfig.Current.ThreadsBaseUrl;

        var sb = new StringBuilder();
        sb.AppendLine("<?xml version=\"1.0\" encoding=\"UTF-8\"?>");
        sb.AppendLine("<urlset xmlns=\"http://www.sitemaps.org/schemas/sitemap/0.9\">");

        AppendUrl(sb, $"{baseUrl}/{PlanetId}", null);

        foreach (var thread in threads)
            AppendUrl(sb, $"{baseUrl}/{PlanetId}/{thread.Id}", thread.EditedTime ?? thread.TimeCreated);

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
