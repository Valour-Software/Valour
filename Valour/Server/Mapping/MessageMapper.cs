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
            Content = message.Content,
            TimeSent = message.TimeSent,
            ChannelId = message.ChannelId,
            EditedTime = message.EditedTime,
            ReplyTo = message.ReplyToMessage?.ToModel(),
            Reactions = message.Reactions?.Select(x => x.ToModel()).ToList(),
            Attachments = attachments,
            Mentions = mentions,
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
            Content = message.Content,
            TimeSent = message.TimeSent,
            ChannelId = message.ChannelId,
            EditedTime = message.EditedTime,
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
