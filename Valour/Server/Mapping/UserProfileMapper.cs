namespace Valour.Server.Mapping;

public static class UserProfileMapper
{
    public static UserProfile ToModel(this Valour.Database.UserProfile profile)
    {
        if (profile is null)
            return null;
        
        return new UserProfile()
        {
            UserId = profile.UserId,
            Headline = profile.Headline,
            Bio = profile.Bio,
            BorderColor = profile.BorderColor,
            AdvancedBorderBackground = profile.AdvancedBorderBackground,
            AnimatedBorder = profile.AnimatedBorder
        };
    }
    
    public static Valour.Database.UserProfile ToDatabase(this UserProfile profile)
    {
        if (profile is null)
            return null;
        
        return new Valour.Database.UserProfile()
        {
            UserId = profile.UserId,
            Headline = profile.Headline,
            Bio = profile.Bio,
            BorderColor = profile.BorderColor,
            AdvancedBorderBackground = profile.AdvancedBorderBackground,
            AnimatedBorder = profile.AnimatedBorder
        };
    }
}