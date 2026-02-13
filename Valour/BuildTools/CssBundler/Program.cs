using System.Text;
using System.Text.RegularExpressions;
using NUglify;
using NUglify.Css;

// Usage: CssBundler <inputs> <scopedAggregator> <searchPaths> <output>
//   inputs:           semicolon-delimited list of global CSS files
//   scopedAggregator: path to the scoped CSS aggregator file (e.g. Valour.Client.styles.css)
//   searchPaths:      semicolon-delimited list of directories to search for @import targets
//   output:           path to write the bundled output

if (args.Length < 4)
{
    Console.Error.WriteLine("Usage: CssBundler <inputs> <scopedAggregator> <searchPaths> <output>");
    return 1;
}

var inputFiles = args[0].Split(';', StringSplitOptions.RemoveEmptyEntries);
var scopedAggregator = args[1];
var searchPaths = args[2].Split(';', StringSplitOptions.RemoveEmptyEntries);
var outputFile = args[3];

var sb = new StringBuilder();

// 1. Append global CSS files
foreach (var path in inputFiles)
{
    if (!File.Exists(path))
    {
        Console.Error.WriteLine($"CSS bundle: file not found: {path}");
        continue;
    }
    sb.Append(File.ReadAllText(path));
    sb.Append('\n');
}

// 2. Append scoped CSS: read the aggregator file and resolve @import lines
if (File.Exists(scopedAggregator))
{
    var importRegex = new Regex(@"@import\s+['""](.+?)['""]\s*;");
    foreach (var line in File.ReadLines(scopedAggregator))
    {
        var match = importRegex.Match(line);
        if (match.Success)
        {
            var importPath = match.Groups[1].Value.TrimStart('/');
            var fileName = Path.GetFileName(importPath);
            bool found = false;
            foreach (var root in searchPaths)
            {
                var trimmed = root.Trim();
                if (string.IsNullOrEmpty(trimmed)) continue;

                // Try direct path join first
                var candidate = Path.Combine(trimmed, importPath);
                if (File.Exists(candidate))
                {
                    Console.WriteLine($"CSS bundle: resolved import {importPath}");
                    sb.Append(File.ReadAllText(candidate));
                    sb.Append('\n');
                    found = true;
                    break;
                }

                // For _content/PackageName/file.css imports, search NuGet package staticwebassets
                if (importPath.StartsWith("_content/") && Directory.Exists(trimmed))
                {
                    var parts = importPath.Split('/');
                    if (parts.Length >= 3)
                    {
                        var pkgName = parts[1].ToLowerInvariant();
                        var pkgDir = Path.Combine(trimmed, pkgName);
                        if (Directory.Exists(pkgDir))
                        {
                            var matches = Directory.GetFiles(pkgDir, fileName, SearchOption.AllDirectories);
                            if (matches.Length > 0)
                            {
                                Console.WriteLine($"CSS bundle: resolved import {importPath} -> {matches[0]}");
                                sb.Append(File.ReadAllText(matches[0]));
                                sb.Append('\n');
                                found = true;
                                break;
                            }
                        }
                    }
                }
            }
            if (!found)
                Console.Error.WriteLine($"CSS bundle: could not resolve import: {importPath}");
        }
        else
        {
            sb.AppendLine(line);
        }
    }
}
else
{
    Console.Error.WriteLine($"CSS bundle: scoped CSS aggregator not found: {scopedAggregator}");
}

// 3. Minify with NUglify (same settings as old BlazorCssBundleService)
var cssSettings = new CssSettings
{
    CommentMode = CssComment.None,
    OutputMode = OutputMode.SingleLine,
    ColorNames = CssColor.Hex,
    Indent = string.Empty,
    TermSemicolons = true,
    RemoveEmptyBlocks = true,
    DecodeEscapes = true,
    MinifyExpressions = true,
};

var combined = sb.ToString();
var minified = Uglify.Css(combined, cssSettings);

if (minified.HasErrors)
{
    foreach (var error in minified.Errors)
        Console.Error.WriteLine($"CSS minification error: {error}");
}

var css = minified.Code ?? combined;
File.WriteAllText(outputFile, css);
Console.WriteLine($"CSS bundle generated: {outputFile} ({css.Length} bytes, from {combined.Length} combined)");

return 0;
