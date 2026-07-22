namespace Valour.Web.StaticExport;

public sealed class StaticExportOptions
{
    public string OutputPath { get; init; } = string.Empty;
    public string SiteBaseUrl { get; init; } = "https://valour.gg";

    public static bool IsExportRequested(string[] args) =>
        args.Any(arg =>
            string.Equals(arg, "export", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(arg, "--export", StringComparison.OrdinalIgnoreCase));

    public static bool TryCreate(string[] args, string contentRootPath, out StaticExportOptions options)
    {
        options = new StaticExportOptions();

        if (!IsExportRequested(args))
            return false;

        var outputPath = Path.Combine(contentRootPath, "dist");
        var siteBaseUrl = "https://valour.gg";

        for (var i = 0; i < args.Length; i++)
        {
            if (IsOption(args[i], "--output", "-o") && i + 1 < args.Length)
            {
                outputPath = ResolvePath(args[++i], contentRootPath);
                continue;
            }

            if (IsOption(args[i], "--base-url", "-b") && i + 1 < args.Length)
            {
                siteBaseUrl = args[++i].TrimEnd('/');
            }
        }

        options = new StaticExportOptions
        {
            OutputPath = outputPath,
            SiteBaseUrl = string.IsNullOrWhiteSpace(siteBaseUrl) ? "https://valour.gg" : siteBaseUrl
        };

        return true;
    }

    private static bool IsOption(string arg, string longName, string shortName) =>
        string.Equals(arg, longName, StringComparison.OrdinalIgnoreCase) ||
        string.Equals(arg, shortName, StringComparison.OrdinalIgnoreCase);

    private static string ResolvePath(string path, string contentRootPath) =>
        Path.GetFullPath(Path.IsPathRooted(path) ? path : Path.Combine(contentRootPath, path));
}
