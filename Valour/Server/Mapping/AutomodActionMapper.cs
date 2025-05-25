namespace Valour.Server.Mapping;

public static class AutomodActionMapper
{
    public static AutomodAction ToModel(this Valour.Database.AutomodAction action)
    {
        if (action is null)
            return null;
        return new AutomodAction
        {
            Id = action.Id,
            PriorAction = action.PriorAction,
            TriggerId = action.TriggerId,
            MemberAddedBy = action.MemberAddedBy,
            ActionType = action.ActionType,
            PlanetId = action.PlanetId,
            TargetMemberId = action.TargetMemberId,
            MessageId = action.MessageId,
            RoleId = action.RoleId,
            Expires = action.Expires,
            Reason = action.Reason,
            Message = action.Message
        };
    }

    public static Valour.Database.AutomodAction ToDatabase(this AutomodAction action)
    {
        if (action is null)
            return null;
        return new Valour.Database.AutomodAction
        {
            Id = action.Id,
            PriorAction = action.PriorAction,
            TriggerId = action.TriggerId,
            MemberAddedBy = action.MemberAddedBy,
            ActionType = action.ActionType,
            PlanetId = action.PlanetId,
            TargetMemberId = action.TargetMemberId,
            MessageId = action.MessageId,
            RoleId = action.RoleId,
            Expires = action.Expires,
            Reason = action.Reason,
            Message = action.Message
        };
    }
}
