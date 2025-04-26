namespace Valour.Server.Mapping;

public static class MessageMapper
{
    public static Message ToModel(this Valour.Database.Message message)
    {
        if (message is null)
            return null;
        
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
            EmbedData = message.EmbedData,
            MentionsData = message.MentionsData,
            AttachmentsData = message.AttachmentsData,
            EditedTime = message.EditedTime,
            ReplyTo = message.ReplyToMessage?.ToModel(),
            Reactions = message.Reactions?.Select(x => x.ToModel()).ToList(),
        };
    }
    
    public static Valour.Database.Message ToDatabase(this Message message)
    {
        if (message is null)
            return null;
        
        return new Valour.Database.Message()
        {
            Id = message.Id,
            PlanetId = message.PlanetId,
            ReplyToId = message.ReplyToId,
            AuthorUserId = message.AuthorUserId,
            AuthorMemberId = message.AuthorMemberId,
            Content = message.Content,
            TimeSent = message.TimeSent,
            ChannelId = message.ChannelId,
            EmbedData = message.EmbedData,
            MentionsData = message.MentionsData,
            AttachmentsData = message.AttachmentsData,
            EditedTime = message.EditedTime,
        };
    }
}