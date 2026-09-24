namespace Valour.Server.Mapping;

public static class MessageMapper
{
    public static Message ToModel(this Valour.Database.Message message)
    {
        if (message is null)
            return null;
        
        var attachments = message.Attachments?
            .OrderBy(x => x.SortOrder)
            .Select(x => x.ToModel())
            .ToList();

        var mentions = message.Mentions?
            .OrderBy(x => x.SortOrder)
            .Select(x => x.ToModel())
            .ToList();

        return new Message()
        {
            Id = message.Id,
            PlanetId = message.PlanetId,
            ReplyToId = message.ReplyToId,
            AuthorUserId = message.AuthorUserId,
            AuthorMemberId = message.AuthorMemberId,
            Content = message.Content ?? string.Empty,
            TimeSent = message.TimeSent,
            ChannelId = message.ChannelId,
            EditedTime = message.EditedTime,
            ImportSource = message.ImportSource,
            WebhookId = message.WebhookId,
            OverrideName = message.OverrideName,
            WebhookAvatarAssetId = message.WebhookAvatarAssetId,
            WebhookAvatarAnimated = message.WebhookAvatarAnimated,
            ReplyTo = message.ReplyToMessage?.ToModel(),
            Reactions = message.Reactions?.Select(x => x.ToModel()).ToList(),
            Attachments = attachments,
            Mentions = mentions,
        };
    }
    
    /// <summary>
    /// Returns a copy of the message that is not a reply. Messages served from the chat cache
    /// are shared by every request, so per-viewer changes must be made on a copy. The copy
    /// shares the reaction, attachment and mention lists, which callers must not modify.
    /// </summary>
    public static Message WithoutReply(this Message message)
    {
        return new Message()
        {
            Id = message.Id,
            PlanetId = message.PlanetId,
            ReplyToId = null,
            AuthorUserId = message.AuthorUserId,
            AuthorMemberId = message.AuthorMemberId,
            Content = message.Content,
            TimeSent = message.TimeSent,
            ChannelId = message.ChannelId,
            Fingerprint = message.Fingerprint,
            EditedTime = message.EditedTime,
            ImportSource = message.ImportSource,
            WebhookId = message.WebhookId,
            OverrideName = message.OverrideName,
            WebhookAvatarAssetId = message.WebhookAvatarAssetId,
            WebhookAvatarAnimated = message.WebhookAvatarAnimated,
            ReplyTo = null,
            Reactions = message.Reactions,
            Attachments = message.Attachments,
            Mentions = message.Mentions,
        };
    }

    public static Valour.Database.Message ToDatabase(this Message message)
    {
        if (message is null)
            return null;
        
        var dbMessage = new Valour.Database.Message()
        {
            Id = message.Id,
            PlanetId = message.PlanetId,
            ReplyToId = message.ReplyToId,
            AuthorUserId = message.AuthorUserId,
            AuthorMemberId = message.AuthorMemberId,
            Content = message.Content ?? string.Empty,
            TimeSent = message.TimeSent,
            ChannelId = message.ChannelId,
            EditedTime = message.EditedTime,
            ImportSource = message.ImportSource,
            WebhookId = message.WebhookId,
            OverrideName = message.OverrideName,
            WebhookAvatarAssetId = message.WebhookAvatarAssetId,
            WebhookAvatarAnimated = message.WebhookAvatarAnimated,
            Reactions = message.Reactions?.Select(x => x.ToDatabase()).ToList(),
            Attachments = message.Attachments?
                .Select((x, i) => x.ToDatabase(message.Id, i))
                .ToList(),
            Mentions = message.Mentions?
                .Select((x, i) => x.ToDatabase(message.Id, i))
                .ToList()
        };

        return dbMessage;
    }
}
