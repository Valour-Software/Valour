namespace Valour.Server.Mapping;

public static class UserMapper
{
    public static User ToModel(this Valour.Database.User user)
    {
        if (user is null)
            return null;
        
        return new User()
        {
            Id = user.Id,
            CustomAvatar = user.CustomAvatar,
            AnimatedAvatar = user.AnimatedAvatar,
            TimeJoined = user.TimeJoined,
            Name = user.Name,
            Tag = user.Tag,
            Bot = user.Bot,
            Disabled = user.Disabled,
            ValourStaff = user.ValourStaff,
            Status = user.Status,
            UserStateCode = user.UserStateCode,
            TimeLastActive = user.TimeLastActive,
            IsMobile = user.IsMobile,
            Compliance = user.Compliance,
            SubscriptionType = user.SubscriptionType
        };
    }
    
    public static Valour.Database.User ToDatabase(this User user)
    {
        if (user is null)
            return null;
        
        return new Valour.Database.User()
        {
            Id = user.Id,
            CustomAvatar = user.CustomAvatar,
            AnimatedAvatar = user.AnimatedAvatar,
            TimeJoined = user.TimeJoined,
            Name = user.Name,
            Tag = user.Tag,
            Bot = user.Bot,
            Disabled = user.Disabled,
            ValourStaff = user.ValourStaff,
            Status = user.Status,
            UserStateCode = user.UserStateCode,
            TimeLastActive = user.TimeLastActive,
            IsMobile = user.IsMobile,
            Compliance = user.Compliance,
            SubscriptionType = user.SubscriptionType
        };
    }
}