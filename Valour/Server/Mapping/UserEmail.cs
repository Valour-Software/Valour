namespace Valour.Server.Mapping;

public static class UserEmailMapper
{
    public static UserEmail ToModel(this Valour.Database.UserEmail email)
    {
        if (email is null)
            return null;
        
        return new UserEmail()
        {
            Email = email.Email,
            Verified = email.Verified,
            UserId = email.UserId
        };
    }
    
    public static Valour.Database.UserEmail ToDatabase(this UserEmail email)
    {
        if (email is null)
            return null;
        
        return new Valour.Database.UserEmail()
        {
            Email = email.Email,
            Verified = email.Verified,
            UserId = email.UserId
        };
    }
}