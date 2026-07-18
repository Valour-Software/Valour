#nullable enable

using System.Text;
using System.Text.RegularExpressions;

namespace Valour.Shared.Models.Wiki;

/// <summary>
/// Slug and vanity-name rules for planet docs. Shared so client-side live
/// validation matches server-side enforcement exactly.
/// </summary>
public static class WikiSlugUtils
{
    /// <summary>
    /// Page slugs: lowercase alphanumerics and single dashes, no leading or
    /// trailing dash, 1-64 chars.
    /// </summary>
    private static readonly Regex SlugRegex =
        new(@"^[a-z0-9](?:-?[a-z0-9])*$", RegexOptions.Compiled);

    /// <summary>
    /// Page slugs that collide with literal public docs routes
    /// </summary>
    public static readonly HashSet<string> ReservedPageSlugs = new(StringComparer.OrdinalIgnoreCase)
    {
        "search", "sitemap", "robots",
    };

    /// <summary>
    /// Derives a URL slug from a page title. Falls back to "page" when the
    /// title has no usable characters. Uniqueness is the caller's concern.
    /// </summary>
    public static string Slugify(string? title)
    {
        if (string.IsNullOrWhiteSpace(title))
            return "page";

        var sb = new StringBuilder(title.Length);
        var lastDash = true; // suppress leading dash

        foreach (var raw in title.Trim())
        {
            var c = char.ToLowerInvariant(raw);
            if (c is >= 'a' and <= 'z' or >= '0' and <= '9')
            {
                sb.Append(c);
                lastDash = false;
            }
            else if (!lastDash)
            {
                sb.Append('-');
                lastDash = true;
            }

            if (sb.Length >= ISharedPlanetWikiPage.MaxSlugLength)
                break;
        }

        var slug = sb.ToString().TrimEnd('-');

        if (slug.Length == 0 || ReservedPageSlugs.Contains(slug))
            return "page";

        return slug;
    }

    public static TaskResult ValidatePageSlug(string? slug)
    {
        if (string.IsNullOrWhiteSpace(slug))
            return TaskResult.FromFailure("Slug is required.");

        if (slug.Length > ISharedPlanetWikiPage.MaxSlugLength)
            return TaskResult.FromFailure($"Slug must be at most {ISharedPlanetWikiPage.MaxSlugLength} characters.");

        if (!SlugRegex.IsMatch(slug))
            return TaskResult.FromFailure("Slugs may only contain lowercase letters, numbers, and single dashes, and cannot start or end with a dash.");

        if (ReservedPageSlugs.Contains(slug))
            return TaskResult.FromFailure($"'{slug}' is a reserved name.");

        return TaskResult.SuccessResult;
    }

}
