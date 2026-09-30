using System.Net;

namespace Valour.Web.StaticExport;

public sealed class StaticSiteExporter
{
    private static readonly ExportPage[] Pages =
    [
        new("Home", "Index", "/", "index.html"),
        new("Home", "DiscordAlternative", "/discord-alternative/", "discord-alternative/index.html"),
        new("Home", "Faq", "/faq/", "faq/index.html"),
        new("Home", "Privacy", "/privacy/", "privacy/index.html"),
        new("Home", "Terms", "/terms/", "terms/index.html"),
        new("Home", "Rules", "/rules/", "rules/index.html"),
        new("Home", "EconomyRules", "/rules/economy/", "rules/economy/index.html"),
        new("Home", "DeleteAccount", "/delete-account/", "delete-account/index.html"),
        new("Home", "Texas", "/texas/", "texas/index.html"),
        new("Home", "UserCount", "/userCount/", "userCount/index.html", InSitemap: false)
    ];

    private readonly RazorViewRenderer _renderer;
    private readonly IWebHostEnvironment _environment;
    private readonly ILogger<StaticSiteExporter> _logger;

    public StaticSiteExporter(
        RazorViewRenderer renderer,
        IWebHostEnvironment environment,
        ILogger<StaticSiteExporter> logger)
    {
        _renderer = renderer;
        _environment = environment;
        _logger = logger;
    }

    public async Task ExportAsync(StaticExportOptions options)
    {
        var outputPath = Path.GetFullPath(options.OutputPath);
        var contentRoot = Path.GetFullPath(_environment.ContentRootPath);
        var webRoot = Path.GetFullPath(_environment.WebRootPath);

        GuardOutputPath(outputPath, contentRoot, webRoot);

        if (Directory.Exists(outputPath))
            Directory.Delete(outputPath, recursive: true);

        Directory.CreateDirectory(outputPath);
        CopyDirectory(webRoot, outputPath);

        foreach (var page in Pages)
        {
            _logger.LogInformation("Rendering {RequestPath}", page.RequestPath);
            var html = await _renderer.RenderAsync(page);
            await WriteTextAsync(outputPath, page.OutputPath, html);
            _logger.LogInformation("Exported {RequestPath} -> {OutputPath}", page.RequestPath, page.OutputPath);
        }

        await WriteTextAsync(outputPath, "sitemap.xml", BuildSitemap(options.SiteBaseUrl));
        await WriteTextAsync(outputPath, "_redirects", BuildRedirects());

        _logger.LogInformation("Static export complete: {OutputPath}", outputPath);
    }

    private static void GuardOutputPath(string outputPath, string contentRoot, string webRoot)
    {
        if (IsSamePath(outputPath, contentRoot))
            throw new InvalidOperationException("Static export output cannot be the project root.");

        if (IsSamePath(outputPath, webRoot))
            throw new InvalidOperationException("Static export output cannot be wwwroot.");

        if (IsSubPathOf(outputPath, webRoot))
            throw new InvalidOperationException("Static export output cannot be inside wwwroot.");

        var root = Path.GetPathRoot(outputPath);
        if (!string.IsNullOrWhiteSpace(root) && IsSamePath(outputPath, root))
            throw new InvalidOperationException("Static export output cannot be a filesystem root.");
    }

    private static void CopyDirectory(string sourcePath, string destinationPath)
    {
        foreach (var sourceFile in Directory.EnumerateFiles(sourcePath, "*", SearchOption.AllDirectories))
        {
            var relativePath = Path.GetRelativePath(sourcePath, sourceFile);
            var destinationFile = Path.Combine(destinationPath, relativePath);
            var destinationDirectory = Path.GetDirectoryName(destinationFile);

            if (!string.IsNullOrWhiteSpace(destinationDirectory))
                Directory.CreateDirectory(destinationDirectory);

            File.Copy(sourceFile, destinationFile, overwrite: true);
        }
    }

    private static async Task WriteTextAsync(string outputRoot, string relativePath, string contents)
    {
        var filePath = Path.Combine(outputRoot, relativePath);
        var directory = Path.GetDirectoryName(filePath);

        if (!string.IsNullOrWhiteSpace(directory))
            Directory.CreateDirectory(directory);

        await File.WriteAllTextAsync(filePath, contents);
    }

    private static string BuildSitemap(string siteBaseUrl)
    {
        var routes = Pages.Where(page => page.InSitemap).Select(page => page.RequestPath).ToArray();
        var urls = routes.Select(route =>
        {
            // Trailing-slash form matches the _redirects rules, so sitemap
            // URLs resolve directly instead of through a 301 hop
            var loc = $"{siteBaseUrl}{(route == "/" ? string.Empty : route.TrimEnd('/') + "/")}";
            var changeFrequency = route == "/" ? "weekly" : "monthly";
            var priority = route == "/" ? "1.0" : "0.7";

            return $"""
              <url>
                <loc>{WebUtility.HtmlEncode(loc)}</loc>
                <changefreq>{changeFrequency}</changefreq>
                <priority>{priority}</priority>
              </url>
            """;
        });

        return $"""
        <?xml version="1.0" encoding="UTF-8"?>
        <urlset xmlns="http://www.sitemaps.org/schemas/sitemap/0.9">
        {string.Join(Environment.NewLine, urls)}
        </urlset>
        """;
    }

    // Invite and planet links can be shared on the root domain. They open in
    // the app, and link previews follow the redirect to the app's own page,
    // which describes the planet. 302 keeps browsers from caching the
    // redirect, so these links can later move without breaking old shares.
    private static string BuildRedirects() =>
        """
        /i/* https://app.valour.gg/i/:splat 302
        /I/* https://app.valour.gg/i/:splat 302
        /d/* https://app.valour.gg/d/:splat 302
        /D/* https://app.valour.gg/d/:splat 302
        /planet/* https://app.valour.gg/planet/:splat 302
        /discord-alternative /discord-alternative/ 301
        /faq /faq/ 301
        /privacy /privacy/ 301
        /terms /terms/ 301
        /rules /rules/ 301
        /rules/economy /rules/economy/ 301
        /delete-account /delete-account/ 301
        /texas /texas/ 301
        /userCount /userCount/ 301
        """;

    private static bool IsSamePath(string left, string right) =>
        string.Equals(NormalizePath(left), NormalizePath(right), StringComparison.OrdinalIgnoreCase);

    private static bool IsSubPathOf(string path, string parent)
    {
        var normalizedPath = NormalizePath(path);
        var normalizedParent = NormalizePath(parent);

        return normalizedPath.StartsWith(
            normalizedParent + Path.DirectorySeparatorChar,
            StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizePath(string path) =>
        Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
}
