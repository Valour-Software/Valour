namespace Valour.Server.Mapping;

public static class MessageReactionMapper
{
    public static MessageReaction ToModel(this Valour.Database.MessageReaction reaction)
    {
        if (reaction is null)
            return null;

        return new MessageReaction()
        {
            Id = reaction.Id,
            Emoji = reaction.Emoji,
            MessageId = reaction.MessageId,
            AuthorUserId = reaction.AuthorUserId,
            AuthorMemberId = reaction.AuthorMemberId,
            CreatedAt = reaction.CreatedAt
        };
    }
    
    public static Valour.Database.MessageReaction ToDatabase(this MessageReaction reaction)
    {
        if (reaction is null)
            return null;

        return new Valour.Database.MessageReaction()
        {
            Id = reaction.Id,
            Emoji = reaction.Emoji,
            MessageId = reaction.MessageId,
            AuthorUserId = reaction.AuthorUserId,
            AuthorMemberId = reaction.AuthorMemberId,
            CreatedAt = reaction.CreatedAt
        };
    }
}