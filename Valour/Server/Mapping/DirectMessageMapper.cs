namespace Valour.Server.Mapping;

public static class DirectMessageMapper
{
    public static DirectMessage ToModel(this Valour.Database.DirectMessage message)
    {
        if (message is null)
            return null;
        
        return new DirectMessage()
        {
            Id = message.Id,
            ReplyToId = message.ReplyToId,
            AuthorUserId = message.AuthorUserId,
            Content = message.Content,
            TimeSent = message.TimeSent,
            ChannelId = message.ChannelId,
            EmbedData = message.EmbedData,
            MentionsData = message.MentionsData,
            AttachmentsData = message.AttachmentsData,
            EditedTime = message.EditedTime
        };
    }
    
    public static Valour.Database.DirectMessage ToDatabase(this DirectMessage message)
    {
        if (message is null)
            return null;
        
        return new Valour.Database.DirectMessage()
        {
            Id = message.Id,
            ReplyToId = message.ReplyToId,
            AuthorUserId = message.AuthorUserId,
            Content = message.Content,
            TimeSent = message.TimeSent,
            ChannelId = message.ChannelId,
            EmbedData = message.EmbedData,
            MentionsData = message.MentionsData,
            AttachmentsData = message.AttachmentsData,
            EditedTime = message.EditedTime
        };
    }
}