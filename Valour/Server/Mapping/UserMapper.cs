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
            HasCustomAvatar = user.HasCustomAvatar,
            HasAnimatedAvatar = user.HasAnimatedAvatar,
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
            SubscriptionType = user.SubscriptionType,
            // Privacy: a hidden prior name is not sent to clients at all.
            // Staff view raw values through the staff lookup endpoint.
            PriorName = user.HidePriorName ? null : user.PriorName,
            NameChangeTime = user.HidePriorName ? null : user.NameChangeTime,
            HidePriorName = user.HidePriorName,
            Version = user.Version,
            TutorialState = user.TutorialState,
            OwnerId = user.OwnerId,
            StarColor1 = user.StarColor1,
            StarColor2 = user.StarColor2
        };
    }

    public static Valour.Database.User ToDatabase(this User user)
    {
        if (user is null)
            return null;

        return new Valour.Database.User()
        {
            Id = user.Id,
            HasCustomAvatar = user.HasCustomAvatar,
            HasAnimatedAvatar = user.HasAnimatedAvatar,
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
            SubscriptionType = user.SubscriptionType,
            PriorName = user.PriorName,
            NameChangeTime = user.NameChangeTime,
            HidePriorName = user.HidePriorName,
            Version = user.Version,
            TutorialState = user.TutorialState,
            OwnerId = user.OwnerId,
            StarColor1 = user.StarColor1,
            StarColor2 = user.StarColor2
        };
    }
}