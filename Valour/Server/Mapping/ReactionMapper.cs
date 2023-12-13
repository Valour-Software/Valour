namespace Valour.Server.Mapping;

public static class ReactionMapper
{
    public static Reaction ToModel(this Valour.Database.Reaction reaction)
    {
        if (reaction is null)
            return null;
        
        return new Reaction()
        {
            Id = reaction.Id,
            MessageId = reaction.MessageId,
            UserId = reaction.UserId,
        };
    }
    
    public static Valour.Database.Reaction ToDatabase(this Reaction reaction)
    {
        if (reaction is null)
            return null;
        
        return new Valour.Database.Reaction()
        {
            Id = reaction.Id,
            MessageId = reaction.MessageId,
            UserId = reaction.UserId,
        };
    }
}