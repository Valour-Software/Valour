using System.Text.RegularExpressions;

namespace Valour.Shared.Utilities;

/// <summary>
/// Utilities for working with custom planet emoji tokens in message text.
/// Token format: «e-:name:~id»
/// </summary>
public static class PlanetEmojiText
{
    public const int MinNameLength = 2;
    public const int MaxNameLength = 32;

    private static readonly Regex NameRegex = new(
        "^[a-z0-9_]{2,32}$",
        RegexOptions.Compiled
    );

    private static readonly Regex TokenRegex = new(
        "«e-:([a-z0-9_]{2,32}):~([0-9]{1,20})»",
        RegexOptions.Compiled
    );

    public static string NormalizeName(string? name)
    {
        return (name ?? string.Empty).Trim().ToLowerInvariant();
    }

    public static bool IsValidName(string? name)
    {
        var normalized = NormalizeName(name);
        return NameRegex.IsMatch(normalized);
    }

    public static string BuildToken(string name, long id)
    {
        var normalized = NormalizeName(name);
        return $"«e-:{normalized}:~{id}»";
    }

    public static bool TryParseToken(string? token, out string name, out long id)
    {
        name = string.Empty;
        id = 0;

        if (string.IsNullOrWhiteSpace(token))
            return false;

        var match = TokenRegex.Match(token);
        if (!match.Success || match.Index != 0 || match.Length != token.Length)
            return false;

        if (!long.TryParse(match.Groups[2].Value, out id))
            return false;

        name = match.Groups[1].Value;
        return true;
    }

    public static HashSet<long> ExtractCustomEmojiIds(string? text)
    {
        HashSet<long> ids = new();
        if (string.IsNullOrWhiteSpace(text))
            return ids;

        var matches = TokenRegex.Matches(text);
        foreach (Match match in matches)
        {
            if (!match.Success)
                continue;

            if (long.TryParse(match.Groups[2].Value, out var id))
                ids.Add(id);
        }

        return ids;
    }
}
