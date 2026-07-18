#nullable enable

using System.Text.RegularExpressions;

namespace Valour.Shared.Models;

public class PlanetVanityRequest
{
    /// <summary>
    /// The vanity name to claim; null or empty clears the planet's vanity
    /// </summary>
    public string? Name { get; set; }
}

/// <summary>
/// Planet vanity name rules. A planet's vanity identifies it in public URLs
/// (wiki pages, the public planet page, and future surfaces like invites).
/// Shared so client-side live validation matches server-side enforcement.
/// </summary>
public static class VanityUtils
{
    /// <summary>
    /// Vanity names: lowercase alphanumerics and single dashes, no leading or
    /// trailing dash, 3-32 chars.
    /// </summary>
    private static readonly Regex VanityRegex =
        new(@"^[a-z0-9](?:-?[a-z0-9]){2,31}$", RegexOptions.Compiled);

    /// <summary>
    /// Names that can never be claimed — first-party subdomains, common
    /// infrastructure paths, and app routes.
    /// </summary>
    public static readonly HashSet<string> ReservedVanityNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "www", "api", "app", "docs", "wiki", "cdn", "public-cdn", "threads", "admin",
        "staff", "valour", "help", "about", "support", "terms", "privacy",
        "blog", "status", "search", "sitemap", "robots", "static", "assets",
        "media", "planet", "planets", "new", "edit", "settings", "login",
        "register", "invite", "discover",
    };

    public static TaskResult ValidateVanity(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return TaskResult.FromFailure("Name is required.");

        if (name.Length is < 3 or > ISharedPlanet.MaxVanityLength)
            return TaskResult.FromFailure($"Vanity names must be 3-{ISharedPlanet.MaxVanityLength} characters.");

        if (!VanityRegex.IsMatch(name))
            return TaskResult.FromFailure("Vanity names may only contain lowercase letters, numbers, and single dashes, and cannot start or end with a dash.");

        // All-digit vanities would be ambiguous with planet ids in public URLs
        if (name.All(char.IsDigit))
            return TaskResult.FromFailure("Vanity names must contain at least one letter.");

        if (ReservedVanityNames.Contains(name))
            return TaskResult.FromFailure($"'{name}' is reserved.");

        return TaskResult.SuccessResult;
    }
}
