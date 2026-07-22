using System.Net;

namespace Valour.Web.StaticExport;

public sealed class StaticSiteExporter
{
    private static readonly ExportPage[] Pages =
    [
        new("Home", "Index", "/", "index.html"),
        new("Home", "Faq", "/faq/", "faq/index.html"),
        new("Home", "UserCount", "/userCount/", "userCount/index.html")
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
        var routes = Pages.Select(page => page.RequestPath).ToArray();
        var urls = routes.Select(route =>
        {
            var loc = $"{siteBaseUrl}{(route == "/" ? string.Empty : route.TrimEnd('/'))}";
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

    private static string BuildRedirects() =>
        """
        /faq /faq/ 301
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
