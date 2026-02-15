using Valour.Shared.Utilities;

namespace Valour.Shared.Models;

public enum PlanetEmojiFormat
{
    Webp128,
    Jpeg128
}

public interface ISharedPlanetEmoji : ISharedPlanetModel<long>
{
    public const int MaxPerPlanet = 20;

    public static string GetBaseRoute(long planetId) => $"api/planets/{planetId}/emojis";
    public static string GetIdRoute(long planetId, long emojiId) => $"{GetBaseRoute(planetId)}/{emojiId}";

    public static string GetCdnPath(long planetId, long emojiId, PlanetEmojiFormat format = PlanetEmojiFormat.Webp128)
    {
        var ext = format == PlanetEmojiFormat.Jpeg128 ? "jpg" : "webp";
        return $"planetemojis/{planetId}/{emojiId}/128.{ext}";
    }

    public static string GetCdnUrl(long planetId, long emojiId, PlanetEmojiFormat format = PlanetEmojiFormat.Webp128)
    {
        return $"https://public-cdn.valour.gg/valour-public/{GetCdnPath(planetId, emojiId, format)}";
    }

    public static string GetCdnUrl(ISharedPlanetEmoji emoji, PlanetEmojiFormat format = PlanetEmojiFormat.Webp128)
    {
        return GetCdnUrl(emoji.PlanetId, emoji.Id, format);
    }

    public static string GetToken(ISharedPlanetEmoji emoji)
    {
        return PlanetEmojiText.BuildToken(emoji.Name, emoji.Id);
    }

    public long CreatorUserId { get; set; }
    public string Name { get; set; }
    public DateTime CreatedAt { get; set; }
}
