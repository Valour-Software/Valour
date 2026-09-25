using Valour.Shared.Models;

namespace Valour.Server.Mapping;

public static class UserMapper
{
    /// <summary>
    /// Returns the user as the given viewer may see it. A user who chose to
    /// appear offline keeps their activity time and device type private from
    /// everyone else, and anonymous callers never see them.
    /// </summary>
    public static User ForViewer(this User user, long? viewerId)
    {
        if (user is null || viewerId == user.Id)
            return user;

        if (viewerId is null || user.UserStateCode == UserState.Offline.Value)
            RedactPresence(user);

        return user;
    }

    /// <summary>
    /// Maps a user for a realtime broadcast, which reaches users other than the
    /// subject. Presence is hidden for users who chose to appear offline.
    /// </summary>
    public static User ToBroadcastModel(this Valour.Database.User user)
    {
        var model = user.ToModel();
        if (model is not null && model.UserStateCode == UserState.Offline.Value)
            RedactPresence(model);

        return model;
    }

    private static void RedactPresence(User user)
    {
        user.TimeLastActive = default;
        user.IsMobile = false;
    }

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
            StarColor2 = user.StarColor2,
            HiddenBadgeFlags = user.HiddenBadgeFlags
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
            StarColor2 = user.StarColor2,
            HiddenBadgeFlags = user.HiddenBadgeFlags
        };
    }
}
