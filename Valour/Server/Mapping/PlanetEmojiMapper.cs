namespace Valour.Server.Mapping;

public static class PlanetEmojiMapper
{
    public static PlanetEmoji ToModel(this Valour.Database.PlanetEmoji emoji)
    {
        if (emoji is null)
            return null;

        return new PlanetEmoji
        {
            Id = emoji.Id,
            PlanetId = emoji.PlanetId,
            CreatorUserId = emoji.CreatorUserId,
            Name = emoji.Name,
            CreatedAt = emoji.CreatedAt
        };
    }

    public static Valour.Database.PlanetEmoji ToDatabase(this PlanetEmoji emoji)
    {
        if (emoji is null)
            return null;

        return new Valour.Database.PlanetEmoji
        {
            Id = emoji.Id,
            PlanetId = emoji.PlanetId,
            CreatorUserId = emoji.CreatorUserId,
            Name = emoji.Name,
            CreatedAt = emoji.CreatedAt
        };
    }
}
