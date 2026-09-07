using System.Text.RegularExpressions;

internal sealed class ContainerQueries
{
    private const string Quoted = "\"(?:\\\\[\\s\\S]|[^\"\\\\])*\"|'(?:\\\\[\\s\\S]|[^'\\\\])*'";
    private const string Comment = @"/\*[\s\S]*?\*/";
    private static readonly Regex Headers = new(
        $"{Comment}|{Quoted}|(?<header>@container\\b(?:{Quoted}|{Comment}|[^{{}}\"'/]|/(?!\\*))*)\\{{",
        RegexOptions.IgnoreCase);
    private readonly string _prefix = "valour_query_" + Guid.NewGuid().ToString("N") + "_";
    private readonly Dictionary<string, string> _headers = new();

    public string Protect(string css) => Headers.Replace(css, match =>
    {
        if (!match.Groups["header"].Success) return match.Value;
        var key = _prefix + _headers.Count;
        _headers.Add(key, match.Groups["header"].Value.TrimEnd());
        return "@container " + key + " {";
    });

    public string Restore(string css)
    {
        var restored = Regex.Replace(css, @"@container\s+(?<key>" + _prefix + @"\d+)\s*(?=\{)",
            match => _headers[match.Groups["key"].Value] + " ");
        if (restored.Contains(_prefix, StringComparison.Ordinal))
            throw new InvalidOperationException("CSS minification damaged a protected container query.");
        return restored;
    }
}
