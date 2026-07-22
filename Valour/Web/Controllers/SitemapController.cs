using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OutputCaching;

namespace Valour.Web.Controllers;

public class SitemapController : Controller
{
    private static readonly string[] Paths = ["/", "/faq", "/userCount"];
    private const string BaseUrl = "https://valour.gg";

    [HttpGet("/sitemap.xml")]
    [ResponseCache(Duration = 86400, Location = ResponseCacheLocation.Any)]
    public IActionResult Index()
    {
        var urls = Paths.Select(path => new SitemapUrl
        {
            Loc = $"{BaseUrl}{path}",
            Changefreq = path == "/" ? "weekly" : "monthly",
            Priority = path == "/" ? "1.0" : "0.7"
        }).ToList();

        var xml = @"<?xml version=""1.0"" encoding=""UTF-8""?>
<urlset xmlns=""http://www.sitemaps.org/schemas/sitemap/0.9"">
" + string.Join("\n", urls.Select(u => $@"  <url>
    <loc>{u.Loc}</loc>
    <changefreq>{u.Changefreq}</changefreq>
    <priority>{u.Priority}</priority>
  </url>")) + @"
</urlset>";

        return Content(xml, "application/xml");
    }

    private class SitemapUrl
    {
        public string Loc { get; set; } = "";
        public string Changefreq { get; set; } = "monthly";
        public string Priority { get; set; } = "0.5";
    }
}
