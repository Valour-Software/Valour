namespace Valour.Server.Mapping;

public static class BlockedUserMapper
{
    public static BlockedUser ToModel(this Valour.Database.BlockedUser blockedUser)
    {
        if (blockedUser is null)
            return null;

        return new BlockedUser()
        {
            SourceUserId = blockedUser.SourceUserId,
            TargetUserId = blockedUser.TargetUserId,
            Reason = blockedUser.Reason,
            Timestamp = blockedUser.Timestamp
        };
    }
    
    public static Valour.Database.BlockedUser ToDatabase(this BlockedUser blockedUser)
    {
        if (blockedUser is null)
            return null;

        return new Valour.Database.BlockedUser()
        {
            SourceUserId = blockedUser.SourceUserId,
            TargetUserId = blockedUser.TargetUserId,
            Reason = blockedUser.Reason,
            Timestamp = blockedUser.Timestamp
        };
    }
}