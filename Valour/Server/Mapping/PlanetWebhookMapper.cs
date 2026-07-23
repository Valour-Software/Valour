namespace Valour.Server.Mapping;

public static class PlanetWebhookMapper
{
    public static PlanetWebhook ToModel(this Valour.Database.PlanetWebhook webhook)
    {
        if (webhook is null)
            return null;

        return new PlanetWebhook()
        {
            Id = webhook.Id,
            PlanetId = webhook.PlanetId,
            ChannelId = webhook.ChannelId,
            Name = webhook.Name,
            AvatarUrl = webhook.AvatarUrl,
            Token = webhook.Token,
            CreatorUserId = webhook.CreatorUserId,
            TimeCreated = webhook.TimeCreated,
        };
    }

    public static Valour.Database.PlanetWebhook ToDatabase(this PlanetWebhook webhook)
    {
        if (webhook is null)
            return null;

        return new Valour.Database.PlanetWebhook()
        {
            Id = webhook.Id,
            PlanetId = webhook.PlanetId,
            ChannelId = webhook.ChannelId,
            Name = webhook.Name,
            AvatarUrl = webhook.AvatarUrl,
            Token = webhook.Token,
            CreatorUserId = webhook.CreatorUserId,
            TimeCreated = webhook.TimeCreated,
        };
    }
}
