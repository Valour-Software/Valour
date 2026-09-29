using Valour.Shared.Models;
using Valour.Shared.Utilities;

namespace Valour.Client.Emojis;

/// <summary>
/// Resolves image sources for custom planet emoji. Unicode emoji are shown as
/// text and drawn by the system's emoji font.
/// </summary>
public class EmojiSourceProvider
{
    private static Dictionary<string, string> _customUrlCache = new();

    public static string GetPlanetEmojiUrl(long planetId, long emojiId)
    {
        var key = $"{planetId}:{emojiId}";
        if (_customUrlCache.TryGetValue(key, out var cached))
            return cached;

        var url = ISharedPlanetEmoji.GetCdnUrl(planetId, emojiId);
        _customUrlCache[key] = url;
        return url;
    }

    /// <summary>
    /// Returns the image source for a reaction that is a custom planet emoji,
    /// or null when the reaction is a Unicode emoji.
    /// </summary>
    public static string? GetCustomSrcFromReaction(string emoji, long? planetId)
    {
        if (planetId is not null && PlanetEmojiText.TryParseToken(emoji, out _, out var customId))
            return GetPlanetEmojiUrl(planetId.Value, customId);

        return null;
    }

    public static string GetAltFromReaction(string emoji)
    {
        if (PlanetEmojiText.TryParseToken(emoji, out var name, out _))
            return $":{name}:";

        return emoji;
    }
}
