namespace Valour.Server.Mapping;

public static class UserFriendMapper
{
    public static UserFriend ToModel(this Valour.Database.UserFriend friend)
    {
        if (friend is null)
            return null;
        
        return new UserFriend()
        {
            Id = friend.Id,
            UserId = friend.UserId,
            FriendId = friend.FriendId
        };
    }
    
    public static Valour.Database.UserFriend ToDatabase(this UserFriend friend)
    {
        if (friend is null)
            return null;
        
        return new Valour.Database.UserFriend()
        {
            Id = friend.Id,
            UserId = friend.UserId,
            FriendId = friend.FriendId
        };
    }
}