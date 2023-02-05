namespace Valour.Server.Mapping;

public static class PlanetMessageMapper
{
    public static PlanetMessage ToModel(this Valour.Database.PlanetMessage message)
    {
        if (message is null)
            return null;
        
        return new PlanetMessage()
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
            Edited = message.Edited
        };
    }
    
    public static Valour.Database.PlanetMessage ToDatabase(this PlanetMessage message)
    {
        if (message is null)
            return null;
        
        return new Valour.Database.PlanetMessage()
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
            Edited = message.Edited
        };
    }
}