using Valour.Client.Components.Utility.EmojiMart;
using Valour.Sdk.Models;
using Valour.Shared.Models;

namespace Valour.Client.Emojis;

public static class PlanetEmojiMapper
{
    public static EmojiClickEvent ToEmojiClickEvent(PlanetEmoji emoji)
    {
        return new EmojiClickEvent
        {
            Aliases = Array.Empty<string>(),
            Id = emoji.Name,
            Keywords = new[] { emoji.Name },
            Name = emoji.Name,
            Native = string.Empty,
            Unified = string.Empty,
            Shortcodes = $":{emoji.Name}:",
            IsCustom = true,
            CustomId = emoji.Id,
            Token = ISharedPlanetEmoji.GetToken(emoji),
            Src = ISharedPlanetEmoji.GetCdnUrl(emoji)
        };
    }

    public static List<EmojiClickEvent> GetPickerItems(Planet? planet)
    {
        if (planet is null)
            return new List<EmojiClickEvent>();

        return planet.Emojis
            .OrderBy(x => x.Name)
            .Select(ToEmojiClickEvent)
            .ToList();
    }

    public static List<EmojiClickEvent> Search(Planet? planet, string query, int maxResults = 10)
    {
        if (planet is null || string.IsNullOrWhiteSpace(query))
            return new List<EmojiClickEvent>();

        var normalized = query.Trim().TrimStart(':').ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(normalized))
            return new List<EmojiClickEvent>();

        return planet.Emojis
            .Where(x => x.Name.Contains(normalized, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(x => x.Name.StartsWith(normalized, StringComparison.OrdinalIgnoreCase))
            .ThenBy(x => x.Name.Length)
            .ThenBy(x => x.Name)
            .Take(maxResults)
            .Select(ToEmojiClickEvent)
            .ToList();
    }
}
