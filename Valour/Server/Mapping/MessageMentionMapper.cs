using Valour.Server.Database;
using Valour.Shared.Models;

namespace Valour.Server.Mapping;

public static class MessageMentionMapper
{
    public static Mention ToModel(this Valour.Database.MessageMention mention)
    {
        if (mention is null)
            return null;

        return new Mention()
        {
            Id = mention.Id,
            MessageId = mention.MessageId,
            SortOrder = mention.SortOrder,
            Type = mention.Type,
            TargetId = mention.TargetId
        };
    }

    public static Valour.Database.MessageMention ToDatabase(this Mention mention, long messageId, int sortOrder)
    {
        if (mention is null)
            return null;

        return new Valour.Database.MessageMention()
        {
            Id = mention.Id == 0 ? IdManager.Generate() : mention.Id,
            MessageId = messageId,
            SortOrder = sortOrder,
            Type = mention.Type,
            TargetId = mention.TargetId
        };
    }
}
