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
        return $"{ValourHosts.PublicCdnBaseUrl}/valour-public/{GetCdnPath(planetId, emojiId, format)}";
    }

    public long CreatorUserId { get; set; }
    public string Name { get; set; }
    public DateTime CreatedAt { get; set; }
}

public static class SharedPlanetEmojiExtensions
{
    extension(ISharedPlanetEmoji emoji)
    {
        public string GetCdnUrl(PlanetEmojiFormat format = PlanetEmojiFormat.Webp128) =>
            ISharedPlanetEmoji.GetCdnUrl(emoji.PlanetId, emoji.Id, format);

        /// <summary>
        /// The token that stands for this emoji in message text.
        /// </summary>
        public string Token => PlanetEmojiText.BuildToken(emoji.Name, emoji.Id);
    }
}
